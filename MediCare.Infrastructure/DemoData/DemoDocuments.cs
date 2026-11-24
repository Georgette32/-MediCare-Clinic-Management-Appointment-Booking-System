using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MediCare.Infrastructure.DemoData;

/// <summary>Builds small but valid PDF and PNG files for the demo medical attachments, without extra packages.</summary>
internal static class DemoDocuments
{
    /// <summary>A one-page A4 PDF with a clinic header, a title and plain text lines. Text must be ASCII.</summary>
    public static byte[] Pdf(string title, IReadOnlyList<string> lines)
    {
        var content = new StringBuilder();
        content.Append("0.10 0.42 0.40 rg 0 782 595 60 re f\n");
        content.Append("BT /F2 20 Tf 1 1 1 rg 40 805 Td (").Append(Escape("MediCare Clinic")).Append(") Tj ET\n");
        content.Append("BT /F2 15 Tf 0 0 0 rg 40 745 Td (").Append(Escape(title)).Append(") Tj ET\n");
        var y = 712;
        foreach (var line in lines)
        {
            content.Append("BT /F1 11 Tf 0.1 0.1 0.1 rg 40 ").Append(y).Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
            y -= 18;
        }
        content.Append("BT /F1 9 Tf 0.45 0.45 0.45 rg 40 40 Td (Demo document generated for the MediCare demo data set.) Tj ET\n");
        var stream = content.ToString();

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}endstream"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n').Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>A 24-bit RGB PNG whose pixels come from the supplied function.</summary>
    public static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[height * (width * 3 + 1)];
        var position = 0;
        for (var y = 0; y < height; y++)
        {
            raw[position++] = 0;
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                raw[position++] = r;
                raw[position++] = g;
                raw[position++] = b;
            }
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            compressed = buffer.ToArray();
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A soft skin-tone photo with a reddish patch, standing in for a clinical skin photo.</summary>
    public static byte[] SkinPhoto() => Png(360, 240, (x, y) =>
    {
        var dx = x - 170.0;
        var dy = y - 125.0;
        var distance = Math.Sqrt(dx * dx * 0.8 + dy * dy * 1.3);
        var shade = (byte)Math.Clamp(222 - y / 12, 0, 255);
        if (distance < 55)
        {
            var mix = 1 - distance / 55;
            return ((byte)Math.Clamp(shade + 10 * mix, 0, 255), (byte)Math.Clamp(170 - 70 * mix, 0, 255), (byte)Math.Clamp(150 - 60 * mix, 0, 255));
        }
        return (shade, (byte)(shade - 46), (byte)(shade - 70));
    });

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        stream.Write(crc);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in first)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (var value in second)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
