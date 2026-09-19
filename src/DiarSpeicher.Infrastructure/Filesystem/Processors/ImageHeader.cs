using System.Buffers.Binary;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static class ImageHeader
{
    public static bool TryRead(ReadOnlySpan<byte> header, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (TryPng(header, ref width, ref height)) return true;
        if (TryGif(header, ref width, ref height)) return true;
        if (TryWebp(header, ref width, ref height)) return true;
        if (TryBmp(header, ref width, ref height)) return true;
        return TryJpeg(header, ref width, ref height);
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool TryPng(ReadOnlySpan<byte> h, ref int width, ref int height)
    {
        if (h.Length < 24) return false;
        if (!h[..8].SequenceEqual(PngSignature)) return false;
        if (!h.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;

        width = BinaryPrimitives.ReadInt32BigEndian(h.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(h.Slice(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryGif(ReadOnlySpan<byte> h, ref int width, ref int height)
    {
        if (h.Length < 10) return false;
        if (!h[..6].SequenceEqual("GIF87a"u8) && !h[..6].SequenceEqual("GIF89a"u8)) return false;

        width = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(6, 2));
        height = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(8, 2));
        return width > 0 && height > 0;
    }

    private static bool TryWebp(ReadOnlySpan<byte> h, ref int width, ref int height)
    {
        if (h.Length < 30) return false;
        if (!h[..4].SequenceEqual("RIFF"u8) || !h.Slice(8, 4).SequenceEqual("WEBP"u8)) return false;

        var chunk = h.Slice(12, 4);

        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (h[23] != 0x9D || h[24] != 0x01 || h[25] != 0x2A) return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(26, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(28, 2)) & 0x3FFF;
        }
        else if (chunk.SequenceEqual("VP8X"u8))
        {
            width = ReadUInt24LittleEndian(h.Slice(24, 3)) + 1;
            height = ReadUInt24LittleEndian(h.Slice(27, 3)) + 1;
        }
        else
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> b) => b[0] | (b[1] << 8) | (b[2] << 16);

    private static bool TryBmp(ReadOnlySpan<byte> h, ref int width, ref int height)
    {
        if (h.Length < 26) return false;
        if (h[0] != (byte)'B' || h[1] != (byte)'M') return false;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(14, 4));

        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadInt16LittleEndian(h.Slice(18, 2));
            height = Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(h.Slice(20, 2)));
        }
        else
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(18, 4));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h.Slice(22, 4)));
        }

        return width > 0 && height > 0;
    }

    private static bool TryJpeg(ReadOnlySpan<byte> h, ref int width, ref int height)
    {
        if (h.Length < 4 || h[0] != 0xFF || h[1] != 0xD8) return false;

        var frame = FindStartOfFrame(h);
        if (frame < 0 || frame + 8 >= h.Length) return false;

        height = BinaryPrimitives.ReadUInt16BigEndian(h.Slice(frame + 5, 2));
        width = BinaryPrimitives.ReadUInt16BigEndian(h.Slice(frame + 7, 2));
        return width > 0 && height > 0;
    }

    private static int FindStartOfFrame(ReadOnlySpan<byte> h)
    {
        var i = 2;

        while (i + 3 < h.Length)
        {
            if (h[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = h[i + 1];

            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (IsStandalone(marker))
            {
                i += 2;
                continue;
            }

            if (marker == StartOfScan) return -1;
            if (IsStartOfFrame(marker)) return i;

            var length = BinaryPrimitives.ReadUInt16BigEndian(h.Slice(i + 2, 2));
            if (length < 2) return -1;

            i += 2 + length;
        }

        return -1;
    }

    private const byte StartOfScan = 0xDA;

    private static bool IsStandalone(byte marker) =>
        marker == 0x01 || marker is >= 0xD0 and <= 0xD9;

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
}
