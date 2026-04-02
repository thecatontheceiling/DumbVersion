using System.Security.Cryptography;

namespace LibDumbVersion;

public static unsafe class HashUtility
{
    private static void ComputeHash(HashAlgorithmName hashAlg, byte* ptr, long length, Span<byte> outputHash)
    {
        using var hasher = IncrementalHash.CreateHash(hashAlg);
        ComputeHashAppend(hasher, ptr, length);
        hasher.GetHashAndReset(outputHash);
    }

    public static void ComputeSHA256(byte* ptr, long length, Span<byte> outputHash)
    {
        ComputeHash(HashAlgorithmName.SHA256, ptr, length, outputHash);
    }

    public static void ComputeHashAppend(IncrementalHash hasher, byte* ptr, long length)
    {
        long hashed = 0;
        while (hashed < length)
        {
            int toHash = (int)Math.Min(1024 * 1024 * 8, length - hashed);
            hasher.AppendData(new ReadOnlySpan<byte>(ptr + hashed, toHash));
            hashed += toHash;
        }
    }
}
