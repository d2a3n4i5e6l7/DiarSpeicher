using System.Security.Cryptography;

namespace DiarSpeicher.Core.Filesystem;

public static class MediaHasher
{
    public const int HashSampleSize = 10000;
    public const int HashSampleCount = 4;

    /// <summary>
    /// Computes the Stump partial SHA-256 hash for a media file.
    /// Matches stump/core/src/filesystem/hash.rs generate().
    /// </summary>
    public static string ComputeStumpHash(string path, long totalBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (totalBytes <= HashSampleSize * HashSampleCount)
        {
            var buffer = new byte[totalBytes];
            int read = stream.Read(buffer, 0, (int)totalBytes);
            sha256.AppendData(buffer, 0, read);
        }
        else
        {
            var buffer = new byte[HashSampleSize];

            for (int i = 0; i < HashSampleCount; i++)
            {
                long offset = (totalBytes / HashSampleCount) * i;
                stream.Seek(offset, SeekOrigin.Begin);
                int read = stream.Read(buffer, 0, HashSampleSize);
                sha256.AppendData(buffer, 0, read);
            }

            long finalOffset = totalBytes - HashSampleSize;
            stream.Seek(finalOffset, SeekOrigin.Begin);
            int finalRead = stream.Read(buffer, 0, HashSampleSize);
            sha256.AppendData(buffer, 0, finalRead);
        }

        var hashBytes = sha256.GetHashAndReset();
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Computes the KOReader MD5 hash algorithm.
    /// Matches stump/core/src/filesystem/hash.rs generate_koreader_hash().
    /// Port of https://github.com/koreader/koreader/blob/master/frontend/util.lua#L1046-L1072
    /// </summary>
    public static string ComputeKoreaderHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        const long step = 1024L;
        const int size = 1024;
        var buffer = new byte[size];

        for (int i = -1; i <= 10; i++)
        {
            long offset = (i == -1) ? 0L : step << (2 * i);
            if (offset >= stream.Length)
            {
                break;
            }

            stream.Seek(offset, SeekOrigin.Begin);
            int bytesRead = stream.Read(buffer, 0, size);
            if (bytesRead == 0)
            {
                break;
            }

            md5.AppendData(buffer, 0, bytesRead);
        }

        var hashBytes = md5.GetHashAndReset();
        return Convert.ToHexStringLower(hashBytes);
    }
}
