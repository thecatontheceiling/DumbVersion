using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LibDumbVersion;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ChunkRecord : IComparable<ChunkRecord>
{
    public ulong Hash;
    public long Offset;

    public int CompareTo(ChunkRecord other)
    {
        int c = Hash.CompareTo(other.Hash);
        return c == 0 ? Offset.CompareTo(other.Offset) : c;
    }
}

public unsafe class BaseFileIndex : IDisposable
{
    public long Length { get; }
    public byte* Ptr { get; private set; }
    public ChunkRecord* Records { get; private set; }
    public int RecordCount { get; }
    public byte[] FullHash { get; }

    private readonly FileStream? fs;
    private readonly MemoryMappedFile? mmf;
    private readonly MemoryMappedViewAccessor? accessor;

    public BaseFileIndex(string basePath)
    {
        Length = new FileInfo(basePath).Length;
        fs = new FileStream(basePath, FileMode.Open, FileAccess.Read, FileShare.Read, 0, FileOptions.SequentialScan);
        mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
        accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        byte* ptr = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        Ptr = ptr;

        try
        {
            using var baseHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            long capacity = (Length / FastCDC.AvgSize) + 10000;
            Records = (ChunkRecord*)NativeMemory.Alloc((nuint)capacity, (nuint)sizeof(ChunkRecord));

            long offset = 0;

            while (offset < Length)
            {
                if (RecordCount >= capacity)
                {
                    capacity *= 2;
                    Records = (ChunkRecord*)NativeMemory.Realloc(Records, (nuint)capacity * (nuint)sizeof(ChunkRecord));
                }

                int remaining = (int)Math.Min(FastCDC.MaxSize, Length - offset);
                int chunkLen = FastCDC.CDCOffset(Ptr + offset, remaining);
                var hash = FastCDC.ComputeHash(Ptr + offset, chunkLen);

                Records[RecordCount++] = new ChunkRecord { Hash = hash, Offset = offset };
                baseHasher.AppendData(new ReadOnlySpan<byte>(Ptr + offset, chunkLen));
                offset += chunkLen;
            }

            FullHash = new byte[32];
            baseHasher.GetHashAndReset(FullHash);

            var sourceSpan = new Span<ChunkRecord>(Records, RecordCount);
            sourceSpan.Sort();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Ptr != null)
        {
            accessor?.SafeMemoryMappedViewHandle.ReleasePointer();
            Ptr = null;
        }

        if (Records != null)
        {
            NativeMemory.Free(Records);
            Records = null;
        }

        accessor?.Dispose();
        mmf?.Dispose();
        fs?.Dispose();

        GC.SuppressFinalize(this);
    }

    ~BaseFileIndex()
    {
        Dispose();
    }
}