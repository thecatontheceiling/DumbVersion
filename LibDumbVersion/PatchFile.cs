using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace LibDumbVersion;

public enum PatchCommand : byte
{
    Copy = 1,
    Insert = 2,
    Eof = 3,
    CopyTarget = 4
}

public struct PatchInstruction
{
    public PatchCommand Command;
    public long Offset;
    public long Length;
}

public class PatchFile : IDisposable
{
    private readonly Stream fileStream;
    private readonly Stream dataStream;
    private string baseFileName = "";
    private byte[] baseFileNameBytes = [];

    public byte[] ExpectedBaseHash { get; set; } = new byte[32];
    public byte[] ExpectedTargetHash { get; set; } = new byte[32];
    public string BaseFileName
    {
        get => baseFileName;
        private set => baseFileName = value.Replace("/", "").Replace("\\", "").Replace(":", "");
    }
    public long TargetSize { get; private set; }

    private byte[] BaseFileNameBytes
    {
        get
        {
            if (!baseFileNameBytes.Any())
            {
                baseFileNameBytes = Encoding.UTF8.GetBytes(baseFileName);
            }

            return baseFileNameBytes;
        }
    }
    private int HeaderSize =>
        MagicBytes.Length
        + sizeof(long)
        + ExpectedBaseHash.Length
        + ExpectedTargetHash.Length
        + sizeof(ushort) // to store length
        + BaseFileNameBytes.Length;

    private static ReadOnlySpan<byte> MagicBytes => "DUMBVER\x01"u8;

    public PatchFile(string path, bool write, string? baseFileName = null, long targetSize = 0)
    {
        if (write)
        {
            BaseFileName = (baseFileName ?? string.Empty);
            TargetSize = targetSize;

            if (BaseFileNameBytes.Length > ushort.MaxValue)
                throw new ArgumentException("Filename bytes length is too long");

            fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
            fileStream.Seek(HeaderSize, SeekOrigin.Begin);
            dataStream = new BrotliStream(fileStream, CompressionLevel.Optimal, leaveOpen: true);
        }
        else
        {
            fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            LoadHeader();
            dataStream = new BrotliStream(fileStream, CompressionMode.Decompress, leaveOpen: true);
        }
    }

    public void Dispose()
    {
        dataStream.Dispose();
        fileStream.Dispose();
        GC.SuppressFinalize(this);
    }

    private static long EncodeZigZag(long value) => (value << 1) ^ (value >> 63);

    private static long DecodeZigZag(long value) => (value >>> 1) ^ -(value & 1);

    private void Write7BitEncodedInt64(long value)
    {
        ulong v = (ulong)value;
        Span<byte> buffer = stackalloc byte[10];
        int count = 0;

        while (v >= 0x80)
        {
            buffer[count++] = (byte)(v | 0x80);
            v >>= 7;
        }

        buffer[count++] = (byte)v;
        dataStream.Write(buffer[..count]);
    }

    private long Read7BitEncodedInt64()
    {
        long result = 0;
        int shift = 0;

        while (true)
        {
            int b = dataStream.ReadByte();
            if (b == -1) throw new EndOfStreamException();

            result |= ((long)(b & 0x7F) << shift);

            if ((b & 0x80) == 0) break;

            shift += 7;
        }

        return result;
    }

    private void LoadHeader()
    {
        byte[] magic = new byte[MagicBytes.Length];
        fileStream.ReadExactly(magic);

        if (!magic.AsSpan().SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid patch file.");

        Span<byte> sizeBuf = stackalloc byte[8];
        fileStream.ReadExactly(sizeBuf);
        TargetSize = BinaryPrimitives.ReadInt64LittleEndian(sizeBuf);

        if (TargetSize is < 0 or > 250L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Invalid target size in patch file.");

        fileStream.ReadExactly(ExpectedBaseHash);
        fileStream.ReadExactly(ExpectedTargetHash);

        Span<byte> fnLenBuf = stackalloc byte[sizeof(ushort)];
        fileStream.ReadExactly(fnLenBuf);
        ushort fnLen = BinaryPrimitives.ReadUInt16LittleEndian(fnLenBuf);

        byte[] srcFn = new byte[fnLen];
        fileStream.ReadExactly(srcFn);
        BaseFileName = Encoding.UTF8.GetString(srcFn);
    }

    public void WriteHeader()
    {
        dataStream.Dispose();

        fileStream.Seek(0, SeekOrigin.Begin);
        fileStream.Write(MagicBytes);

        Span<byte> targetLenSpan = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(targetLenSpan, TargetSize);
        fileStream.Write(targetLenSpan);

        fileStream.Write(ExpectedBaseHash);
        fileStream.Write(ExpectedTargetHash);

        Span<byte> fnLenSpan = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(fnLenSpan, (ushort)BaseFileNameBytes.Length);
        fileStream.Write(fnLenSpan);
        fileStream.Write(BaseFileNameBytes);

        fileStream.Flush();
    }

    public IEnumerable<PatchInstruction> GetInstrs()
    {
        while (true)
        {
            int b = dataStream.ReadByte();
            if (b == -1) yield break;

            PatchInstruction instr = new PatchInstruction();
            PatchCommand cmd = (PatchCommand)b;
            instr.Command = cmd;

            if (cmd == PatchCommand.Eof)
            {
                yield return instr;
                yield break;
            }

            switch (cmd)
            {
                case PatchCommand.Copy:
                    instr.Offset = DecodeZigZag(Read7BitEncodedInt64());
                    instr.Length = Read7BitEncodedInt64();
                    break;
                case PatchCommand.CopyTarget:
                    instr.Offset = Read7BitEncodedInt64();
                    instr.Length = Read7BitEncodedInt64();
                    break;
                case PatchCommand.Insert:
                    instr.Length = Read7BitEncodedInt64();
                    break;
            }

            yield return instr;
        }
    }

    public void AddCopy(long relativeOffset, long length)
    {
        dataStream.WriteByte((byte)PatchCommand.Copy);
        Write7BitEncodedInt64(EncodeZigZag(relativeOffset));
        Write7BitEncodedInt64(length);
    }

    public void AddCopyTarget(long distance, long length)
    {
        dataStream.WriteByte((byte)PatchCommand.CopyTarget);
        Write7BitEncodedInt64(distance);
        Write7BitEncodedInt64(length);
    }

    public void AddInsert(long length)
    {
        dataStream.WriteByte((byte)PatchCommand.Insert);
        Write7BitEncodedInt64(length);
    }

    public void AddEof()
    {
        dataStream.WriteByte((byte)PatchCommand.Eof);
    }

    public unsafe void ReadData(byte* targetPtr, int length)
    {
        dataStream.ReadExactly(new Span<byte>(targetPtr, length));
    }

    public unsafe void WriteData(byte* targetPtr, int length)
    {
        dataStream.Write(new ReadOnlySpan<byte>(targetPtr, length));
    }
}
