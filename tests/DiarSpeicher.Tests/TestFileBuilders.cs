using System.IO.Compression;
using System.Text;

namespace DiarSpeicher.Tests;

internal static class PngBuilder
{
    public static byte[] Solid(int width, int height)
    {
        var raw = new List<byte>(height * (width * 3 + 1));
        for (var y = 0; y < height; y++)
        {
            raw.Add(0);
            for (var x = 0; x < width; x++)
            {
                raw.Add(200);
                raw.Add(40);
                raw.Add(40);
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new List<byte>();
        header.AddRange(BigEndian(width));
        header.AddRange(BigEndian(height));
        header.AddRange([8, 2, 0, 0, 0]);

        WriteChunk(output, "IHDR", [.. header]);
        WriteChunk(output, "IDAT", Deflate([.. raw]));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(BigEndian(data.Length));
        stream.Write(typeBytes);
        stream.Write(data);
        stream.Write(BigEndian(unchecked((int)Crc32([.. typeBytes, .. data]))));
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ];

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// Builds a minimal but structurally valid PDF, enough for a reader to report the page count.
/// </summary>
internal static class PdfBuilder
{
    public static byte[] WithPages(int pageCount)
    {
        using var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        Write(output, "%PDF-1.4\n");

        var pageNumbers = Enumerable.Range(3, pageCount).ToArray();
        var kids = string.Join(' ', pageNumbers.Select(n => $"{n} 0 R"));

        WriteObject(output, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(output, offsets, 2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");

        foreach (var number in pageNumbers)
        {
            WriteObject(output, offsets, number, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 600] /Resources << >> >>");
        }

        var startXref = output.Position;
        var size = offsets.Keys.Max() + 1;

        Write(output, $"xref\n0 {size}\n0000000000 65535 f \n");
        for (var i = 1; i < size; i++)
        {
            Write(output, $"{offsets.GetValueOrDefault(i, 0):D10} 00000 n \n");
        }

        Write(output, $"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");

        return output.ToArray();
    }

    private static void WriteObject(Stream stream, Dictionary<int, long> offsets, int number, string body)
    {
        offsets[number] = stream.Position;
        Write(stream, $"{number} 0 obj\n{body}\nendobj\n");
    }

    private static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
