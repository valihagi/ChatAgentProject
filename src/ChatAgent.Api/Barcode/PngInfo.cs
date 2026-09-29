using System.Buffers.Binary;

namespace ChatAgent.Api.Barcode;

public static class PngInfo
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Width in pixels from the IHDR chunk, or null if the bytes are not a PNG.</summary>
    public static int? WidthPx(byte[] png)
    {
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(Signature)) return null;
        return (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
    }
}
