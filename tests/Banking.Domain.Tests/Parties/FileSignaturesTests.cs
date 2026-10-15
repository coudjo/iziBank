using Banking.Domain.Parties;

namespace Banking.Domain.Tests.Parties;

public sealed class FileSignaturesTests
{
    [Fact]
    public void Types_come_from_the_leading_bytes()
    {
        FileSignatures.Detect([0xFF, 0xD8, 0xFF, 0xE1]).ShouldBe(FileSignatures.Jpeg);
        FileSignatures.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]).ShouldBe(FileSignatures.Png);
        FileSignatures.Detect("%PDF-1.4"u8).ShouldBe(FileSignatures.Pdf);
        FileSignatures.Detect("GIF89a"u8).ShouldBeNull();
        FileSignatures.Detect([]).ShouldBeNull();
    }

    [Fact]
    public void Png_dimensions_are_read_from_the_header()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0x07, 0xD0, 0, 0, 0x03, 0xE8];
        FileSignatures.PngSize(png).ShouldBe((2000, 1000));
        FileSignatures.PngSize(png.AsSpan(0, 20)).ShouldBeNull();
    }
}
