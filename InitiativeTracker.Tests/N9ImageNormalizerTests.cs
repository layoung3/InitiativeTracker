using System.Buffers.Binary;
using System.Text;
using InitiativeTracker.Web.Services;
using SkiaSharp;

namespace InitiativeTracker.Tests;

public sealed class N9ImageNormalizerTests
{
    private static byte[] OrientedJpeg(ushort orientation)
    {
        using var bitmap = new SKBitmap(20, 30);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            using var green = new SKPaint { Color = SKColors.Lime };
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, 10, 15, red);
            canvas.DrawRect(10, 0, 10, 15, green);
            canvas.DrawRect(0, 15, 10, 15, white);
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = encoded.ToArray();
        using var data = new MemoryStream();
        using (var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("Exif\0\0"u8); writer.Write("II"u8); writer.Write((ushort)42); writer.Write((uint)8);
            writer.Write((ushort)2); writer.Write((ushort)0x112); writer.Write((ushort)3); writer.Write((uint)1);
            writer.Write(orientation); writer.Write((ushort)0);
            writer.Write((ushort)0x8825); writer.Write((ushort)4); writer.Write((uint)1); writer.Write((uint)38);
            writer.Write((uint)0);
            var gps=Encoding.ASCII.GetBytes("GPS-private-fixture\0");
            writer.Write((ushort)1); writer.Write((ushort)0x001B); writer.Write((ushort)2); writer.Write((uint)gps.Length); writer.Write((uint)56);
            writer.Write((uint)0); writer.Write(gps);
        }
        var exif = data.ToArray(); var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(exif.Length + 2));
        return jpeg[..2].Concat(new byte[] { 255, 225 }).Concat(length).Concat(exif).Concat(jpeg[2..]).ToArray();
    }

    [Theory]
    [InlineData(1,0,0)] [InlineData(2,19,0)] [InlineData(3,19,29)] [InlineData(4,0,29)]
    [InlineData(5,0,0)] [InlineData(6,0,29)] [InlineData(7,19,29)] [InlineData(8,19,0)]
    public void AllExifOrientationsTransformPixelsAndStripSourceMetadata(int orientation, int sourceX, int sourceY)
    {
        var input = OrientedJpeg((ushort)orientation);
        using var original = SKBitmap.Decode(OrientedJpeg(1));
        var expected = original.GetPixel(sourceX, sourceY);
        var normalized = new ImageNormalizer().Normalize(input, "portrait");
        using var result = SKBitmap.Decode(normalized.Bytes);
        Assert.Equal(orientation > 4 ? 30 : 20, result.Width);
        Assert.Equal(orientation > 4 ? 20 : 30, result.Height);
        var pixel = result.GetPixel(0, 0);
        Assert.InRange(Math.Abs(expected.Red - pixel.Red), 0, 3);
        Assert.InRange(Math.Abs(expected.Green - pixel.Green), 0, 3);
        Assert.InRange(Math.Abs(expected.Blue - pixel.Blue), 0, 3);
        var output = Encoding.Latin1.GetString(normalized.Bytes);
        Assert.DoesNotContain("GPS-private-fixture", output); Assert.DoesNotContain("Exif", output);
        using var data = SKData.CreateCopy(normalized.Bytes); using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
    }

    [Fact]
    public void CorruptPngCrcAndRasterCorpusAreRejected()
    {
        var corrupt = N9MediaTests.Raster(); corrupt[^5] ^= 1;
        Assert.Throws<MediaValidationException>(() => new ImageNormalizer().Normalize(corrupt, "portrait"));
        var random = new Random(919);
        for (int i=0;i<30;i++)
        {
            var bytes = new byte[100+i]; random.NextBytes(bytes);
            if (i%2 == 0) {bytes[0]=255;bytes[1]=216;bytes[^2]=255;bytes[^1]=217;}
            Assert.Throws<MediaValidationException>(() => new ImageNormalizer().Normalize(bytes,"portrait"));
        }
    }

    [Fact]
    public void PublishedArtifactSmokeUsesRealNativeDecoder() => ImageSmokeTest.Run();
}
