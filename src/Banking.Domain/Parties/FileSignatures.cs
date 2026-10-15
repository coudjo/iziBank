namespace Banking.Domain.Parties;

/// <summary>Plan 6.6: the type of an upload comes from its leading bytes, never from its name or declared type.</summary>
public static class FileSignatures
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Pdf = "application/pdf";

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> PdfMagic => [0x25, 0x50, 0x44, 0x46, 0x2D];

    /// <summary>The detected content type, or null when the bytes are not a JPEG, PNG or PDF.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngMagic))
        {
            return Png;
        }

        if (content.StartsWith(JpegMagic))
        {
            return Jpeg;
        }

        return content.StartsWith(PdfMagic) ? Pdf : null;
    }

    /// <summary>Width and height from a PNG's IHDR chunk, or null when the header is not a valid PNG.</summary>
    public static (int Width, int Height)? PngSize(ReadOnlySpan<byte> content)
    {
        // 8-byte signature, 4-byte length, "IHDR", then 4-byte big-endian width and height.
        if (content.Length < 24 || !content.StartsWith(PngMagic)
            || content[12] != (byte)'I' || content[13] != (byte)'H' || content[14] != (byte)'D' || content[15] != (byte)'R')
        {
            return null;
        }

        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(content.Slice(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(content.Slice(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }
}
