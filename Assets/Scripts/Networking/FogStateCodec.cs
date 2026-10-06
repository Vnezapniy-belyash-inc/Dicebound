using System;
using System.IO;
using System.IO.Compression;

/// <summary>Small sparse masks must not flood the reliable transport with grid-sized zero buffers.</summary>
public static class FogStateCodec
{
    public static byte[] Encode(bool[] visible, bool[] explored)
    {
        using var result = new MemoryStream();
        using (var compressor = new DeflateStream(result, CompressionLevel.Fastest, true))
        {
            var a = FogVisibility.Pack(visible); var b = FogVisibility.Pack(explored);
            compressor.Write(a, 0, a.Length); compressor.Write(b, 0, b.Length);
        }
        return result.ToArray();
    }
    public static void Decode(byte[] payload, int count, out bool[] visible, out bool[] explored)
    {
        int length = (count + 7) / 8;
        var packed = new byte[length * 2];
        using var input = new MemoryStream(payload, false);
        using var stream = new DeflateStream(input, CompressionMode.Decompress);
        int offset = 0;
        while (offset < packed.Length)
        {
            int read = stream.Read(packed, offset, packed.Length - offset);
            if (read == 0) throw new FormatException("Обрезанное состояние тумана.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new FormatException("Некорректный размер состояния тумана.");
        var a = new byte[length]; var b = new byte[length];
        Buffer.BlockCopy(packed, 0, a, 0, length); Buffer.BlockCopy(packed, length, b, 0, length);
        visible = FogVisibility.Unpack(a, count); explored = FogVisibility.Unpack(b, count);
    }
}
