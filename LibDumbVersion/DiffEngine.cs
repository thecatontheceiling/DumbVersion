using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LibDumbVersion;

public unsafe class DiffEngine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetMatchLength(byte* ptr1, byte* ptr2, long maxLength)
    {
        long matchLen = 0;

        while (maxLength - matchLen >= 8)
        {
            ulong diff = Unsafe.ReadUnaligned<ulong>(ptr1 + matchLen) ^ Unsafe.ReadUnaligned<ulong>(ptr2 + matchLen);
            if (diff == 0)
            {
                matchLen += 8;
            }
            else
            {
                if (BitConverter.IsLittleEndian)
                    matchLen += (uint)BitOperations.TrailingZeroCount(diff) / 8;
                else
                    matchLen += (uint)BitOperations.LeadingZeroCount(diff) / 8;

                return matchLen;
            }
        }

        while (matchLen < maxLength && ptr1[matchLen] == ptr2[matchLen])
        {
            matchLen++;
        }

        return matchLen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetBackwardMatchLength(byte* ptr1, byte* ptr2, long maxLength)
    {
        long matchLen = 0;

        while (maxLength - matchLen >= 8)
        {
            ulong diff = Unsafe.ReadUnaligned<ulong>(ptr1 - matchLen - 8) ^ Unsafe.ReadUnaligned<ulong>(ptr2 - matchLen - 8);
            if (diff == 0)
            {
                matchLen += 8;
            }
            else
            {
                if (BitConverter.IsLittleEndian)
                    matchLen += (uint)BitOperations.LeadingZeroCount(diff) / 8;
                else
                    matchLen += (uint)BitOperations.TrailingZeroCount(diff) / 8;

                return matchLen;
            }
        }

        while (matchLen < maxLength && ptr1[-(matchLen + 1)] == ptr2[-(matchLen + 1)])
        {
            matchLen++;
        }

        return matchLen;
    }

    public static void CreatePatch(string basePath, string targetPath, string patchPath)
    {
        Console.WriteLine("Indexing base...");
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        using var baseIndex = new BaseFileIndex(basePath);

        Console.WriteLine($"Base file indexed: {baseIndex.RecordCount} unique chunks");
        Console.WriteLine($"Took {stopwatch.Elapsed.TotalSeconds:0.00}s");

        CreatePatch(baseIndex, basePath, targetPath, patchPath);
    }

    public static void CreatePatch(BaseFileIndex baseIndex, string basePath, string targetPath, string patchPath)
    {
        long targetLength = new FileInfo(targetPath).Length;

        using var targetFsMapping = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan);
        using var targetMmf = MemoryMappedFile.CreateFromFile(targetFsMapping, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
        using var targetAccessor = targetMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        byte* targetPtr = null;

        try
        {
            targetAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref targetPtr);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Console.WriteLine("Generating Patch...");

            var sourceSpan = new ReadOnlySpan<ChunkRecord>(baseIndex.Records, baseIndex.RecordCount);

            const int TargetCacheBits = 20;
            const int TargetCacheSize = 1 << TargetCacheBits;
            const int TargetCacheMask = TargetCacheSize - 1;

            ulong* targetCacheKeys = null;
            long* targetCacheValues = null;

            try
            {
                targetCacheKeys = (ulong*)NativeMemory.AllocZeroed(TargetCacheSize, sizeof(ulong));
                targetCacheValues = (long*)NativeMemory.AllocZeroed(TargetCacheSize, sizeof(long));

                using var patch = new PatchFile(patchPath, write: true, baseFileName: Path.GetFileName(basePath), targetSize: targetLength);

                using var targetHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                long offset = 0;
                bool isInserting = false;
                long insertStartOffset = 0, insertLength = 0, lastBaseOffset = 0;

                void FlushInsert()
                {
                    if (!isInserting) return;

                    if (insertLength > 0)
                    {
                        patch.AddInsert(insertLength);

                        long remainingLocal = insertLength;
                        long currentOffset = insertStartOffset;
                        while (remainingLocal > 0)
                        {
                            int toWrite = (int)Math.Min(8 * 1024 * 1024, remainingLocal);
                            patch.WriteData(targetPtr + currentOffset, toWrite);
                            remainingLocal -= toWrite;
                            currentOffset += toWrite;
                        }
                    }

                    isInserting = false;
                }

                const int StrictThreshold = FastCDC.AvgSize;

                while (offset < targetLength)
                {
                    int remaining = (int)Math.Min(FastCDC.MaxSize, targetLength - offset);
                    int chunkLen = FastCDC.CDCOffset(targetPtr + offset, remaining);
                    var hash = FastCDC.ComputeHash(targetPtr + offset, chunkLen);

                    var searchRecord = new ChunkRecord { Hash = hash, Offset = long.MinValue };
                    int idx = sourceSpan.BinarySearch(searchRecord);
                    if (idx < 0) idx = ~idx;

                    long bestBaseOffset = -1;
                    long bestMatchLen = 0;

                    if (idx < sourceSpan.Length && sourceSpan[idx].Hash == hash)
                    {
                        int currentIdx = idx;

                        while (currentIdx < sourceSpan.Length && sourceSpan[currentIdx].Hash == hash)
                        {
                            long baseMatchOffset = sourceSpan[currentIdx].Offset;
                            long maxPossibleExpand = Math.Min(baseIndex.Length - baseMatchOffset, targetLength - offset);
                            long matchLen =
                                GetMatchLength(baseIndex.Ptr + baseMatchOffset, targetPtr + offset, maxPossibleExpand);

                            if (matchLen > bestMatchLen)
                            {
                                bestMatchLen = matchLen;
                                bestBaseOffset = baseMatchOffset;

                                // If we hit max bounds, just exit the loop to save time
                                if (matchLen == maxPossibleExpand) break;
                            }

                            currentIdx++;
                        }
                    }

                    long bestTargetOffset = -1;
                    long bestTargetMatchLen = 0;

                    int cacheIdx = (int)(hash & TargetCacheMask);
                    if (targetCacheKeys[cacheIdx] == hash)
                    {
                        long tgtOffset = targetCacheValues[cacheIdx];
                        long maxPossibleExpand = targetLength - offset;
                        bestTargetMatchLen =
                            GetMatchLength(targetPtr + tgtOffset, targetPtr + offset, maxPossibleExpand);
                        bestTargetOffset = tgtOffset;
                    }

                    long maxMatchLen = 0;
                    bool useBase = false;

                    if (bestMatchLen >= bestTargetMatchLen && bestMatchLen > 0)
                    {
                        maxMatchLen = bestMatchLen;
                        useBase = true;
                    }
                    else if (bestTargetMatchLen > 0)
                    {
                        maxMatchLen = bestTargetMatchLen;
                        useBase = false;
                    }

                    bool acceptMatch = false;

                    if (maxMatchLen > 0)
                    {
                        if (isInserting)
                            acceptMatch = maxMatchLen >= StrictThreshold;
                        else
                            acceptMatch = maxMatchLen >= chunkLen;
                    }

                    if (acceptMatch)
                    {
                        long forwardMatchLen = maxMatchLen, backMatch = 0;

                        if (isInserting)
                        {
                            long maxBack = insertLength;
                            maxBack = Math.Min(maxBack, useBase ? bestBaseOffset : bestTargetOffset);

                            backMatch = GetBackwardMatchLength(
                                useBase ? baseIndex.Ptr + bestBaseOffset : targetPtr + bestTargetOffset,
                                targetPtr + offset,
                                maxBack
                            );
                        }

                        if (backMatch > 0)
                        {
                            insertLength -= backMatch;
                            if (useBase) bestBaseOffset -= backMatch;
                            else bestTargetOffset -= backMatch;
                            maxMatchLen += backMatch;
                        }

                        FlushInsert();

                        if (useBase)
                        {
                            long relativeOffset = bestBaseOffset - lastBaseOffset;
                            patch.AddCopy(relativeOffset, maxMatchLen);

                            lastBaseOffset = bestBaseOffset + maxMatchLen;
                        }
                        else
                        {
                            long currentWritePointer = offset - backMatch;
                            long distance = currentWritePointer - bestTargetOffset;

                            patch.AddCopyTarget(distance, maxMatchLen);
                        }

                        // forwardMatchLen because the backward match portion was already added
                        HashUtility.ComputeHashAppend(targetHasher, targetPtr + offset, forwardMatchLen);
                        offset += forwardMatchLen;
                    }
                    else
                    {
                        if (!isInserting)
                        {
                            isInserting = true;
                            insertStartOffset = offset;
                            insertLength = chunkLen;
                        }
                        else
                        {
                            insertLength += chunkLen;
                        }

                        targetCacheKeys[cacheIdx] = hash;
                        targetCacheValues[cacheIdx] = offset;

                        targetHasher.AppendData(new ReadOnlySpan<byte>(targetPtr + offset, chunkLen));
                        offset += chunkLen;
                    }
                }

                FlushInsert();

                patch.AddEof();

                Span<byte> targetFullHash = stackalloc byte[32];
                targetHasher.GetHashAndReset(targetFullHash);

                patch.ExpectedBaseHash = (byte[])baseIndex.FullHash.Clone();
                patch.ExpectedTargetHash = targetFullHash.ToArray();

                patch.WriteHeader();

                Console.WriteLine("Patch created");
                Console.WriteLine($"Took {stopwatch.Elapsed.TotalSeconds:0.00}s");

            }
            finally
            {
                if (targetCacheKeys != null) NativeMemory.Free(targetCacheKeys);
                if (targetCacheValues != null) NativeMemory.Free(targetCacheValues);
                stopwatch.Stop();
            }
        }
        finally
        {
            if (targetPtr != null) targetAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public static void ApplyPatch(string baseIsoPath, string patchPath, string targetIsoPath, Action<int> progressCallback, ref byte[]? knownBaseHash)
    {
        using var patch = new PatchFile(patchPath, write: false);
        var targetSize = patch.TargetSize;

        Console.WriteLine($"\nBase File Hash: {Convert.ToHexString(patch.ExpectedBaseHash)}");
        Console.WriteLine($"Output File Hash: {Convert.ToHexString(patch.ExpectedTargetHash)}");
        Console.WriteLine($"[1/2] Validating Base: {Path.GetFileName(baseIsoPath)}...");

        long baseLength = new FileInfo(baseIsoPath).Length;

        using var baseFs = new FileStream(baseIsoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 0,
            FileOptions.SequentialScan);
        using var baseMmf = MemoryMappedFile.CreateFromFile(baseFs, null, 0, MemoryMappedFileAccess.Read,
            HandleInheritability.None, false);
        using var baseAccessor = baseMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        if (targetSize == 0)
        {
            using var emptyFs = new FileStream(targetIsoPath, FileMode.Create, FileAccess.Write, FileShare.None);
            return;
        }

        using var targetFs = new FileStream(targetIsoPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            0, FileOptions.SequentialScan);
        targetFs.SetLength(targetSize);
        using var targetMmf = MemoryMappedFile.CreateFromFile(targetFs, null, targetSize,
            MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        using var targetAccessor = targetMmf.CreateViewAccessor(0, targetSize, MemoryMappedFileAccess.ReadWrite);

        byte* basePtr = null;
        byte* targetPtr = null;

        try
        {
            baseAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            targetAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref targetPtr);

            Span<byte> actualBaseHash = stackalloc byte[32];

            if (knownBaseHash is { Length: 32 })
            {
                knownBaseHash.AsSpan().CopyTo(actualBaseHash);
            }
            else
            {
                HashUtility.ComputeSHA256(basePtr, baseLength, actualBaseHash);
                knownBaseHash = actualBaseHash.ToArray();
            }

            if (!actualBaseHash.SequenceEqual(patch.ExpectedBaseHash))
                throw new Exception("This file is not the correct base file.");

            Console.WriteLine("[2/2] Applying patch...");

            using var targetHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            long bytesWritten = 0, lastBaseOffset = 0;
            int lastProgress = -1;

            foreach (PatchInstruction instr in patch.GetInstrs())
            {
                var cmd = instr.Command;

                if (cmd == PatchCommand.Eof) break;

                long originalBytesWritten = bytesWritten;

                switch (cmd)
                {
                    case PatchCommand.Copy:
                        {
                            long relativeOffset = instr.Offset;
                            long srcOffset = lastBaseOffset + relativeOffset;

                            long length = instr.Length;

                            if (srcOffset < 0 || length < 0 || baseLength - srcOffset < length)
                                throw new InvalidDataException("Out-of-bounds copy command");
                            if (targetSize - bytesWritten < length)
                                throw new InvalidDataException("Copy command exceeds target size");

                            Buffer.MemoryCopy(basePtr + srcOffset, targetPtr + bytesWritten, targetSize - bytesWritten, length);
                            bytesWritten += length;
                            lastBaseOffset = srcOffset + length;
                            break;
                        }
                    case PatchCommand.CopyTarget:
                        {
                            long distance = instr.Offset;
                            long length = instr.Length;

                            long srcOffset = bytesWritten - distance;

                            if (distance <= 0 || srcOffset < 0 || length < 0)
                                throw new InvalidDataException("Out-of-bounds target deduplication command");
                            if (targetSize - bytesWritten < length)
                                throw new InvalidDataException("CopyTarget command exceeds target size");

                            if (distance < length)
                            {
                                long remaining = length;
                                long currentDst = bytesWritten;
                                long chunkSize = distance;

                                while (remaining > 0)
                                {
                                    long toCopy = Math.Min(chunkSize, remaining);
                                    Buffer.MemoryCopy(targetPtr + srcOffset, targetPtr + currentDst, targetSize - currentDst, toCopy);
                                    currentDst += toCopy;
                                    remaining -= toCopy;
                                    chunkSize += toCopy;
                                }
                            }
                            else
                            {
                                Buffer.MemoryCopy(targetPtr + srcOffset, targetPtr + bytesWritten, targetSize - bytesWritten, length);
                            }

                            bytesWritten += length;

                            break;
                        }
                    case PatchCommand.Insert:
                        {
                            long length = instr.Length;

                            if (length < 0 || targetSize - bytesWritten < length)
                                throw new InvalidDataException("Invalid insert command.");

                            long remaining = length;
                            long currentOffset = bytesWritten;
                            while (remaining > 0)
                            {
                                int toRead = (int)Math.Min(8 * 1024 * 1024, remaining);
                                patch.ReadData(targetPtr + currentOffset, toRead);
                                remaining -= toRead;
                                currentOffset += toRead;
                            }

                            bytesWritten += length;
                            break;
                        }
                }

                HashUtility.ComputeHashAppend(targetHasher, targetPtr + originalBytesWritten, instr.Length);

                int progress = (int)((bytesWritten * 100) / targetSize);
                if (progress == lastProgress) continue;
                progressCallback(progress);
                lastProgress = progress;
            }

            Span<byte> actualTargetHash = stackalloc byte[32];
            targetHasher.GetHashAndReset(actualTargetHash);

            if (!actualTargetHash.SequenceEqual(patch.ExpectedTargetHash))
            {
                throw new Exception("Patch applied, but final validation failed. The output file is corrupted.");
            }
        }
        finally
        {
            if (basePtr != null) baseAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            if (targetPtr != null) targetAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }
}