using System.Buffers.Binary;
using SkiaSharp;

namespace InitiativeTracker.Web.Services;

public sealed class MediaValidationException(string message) : Exception(message);
public sealed class MediaBusyException() : Exception("Another image is being processed. Try again shortly.");
public sealed record NormalizedImage(byte[] Bytes, int Width, int Height);

public sealed class ImageNormalizer
{
    private static readonly uint[] CrcTable = CreateCrcTable();
    public const int MaxUploadBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 12_000_000;
    public const int MaxOutputBytes = 3 * 1024 * 1024;

    public NormalizedImage Normalize(byte[] input, string kind)
    {
        if (input.Length == 0 || input.Length > MaxUploadBytes) throw Invalid();
        ValidateContainer(input);
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)
            || codec.FrameCount > 1) throw Invalid();
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new MediaValidationException("Image exceeds 4096 pixels per side or 12 million total pixels.");
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var decoded = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) throw Invalid();
        var origin = codec.EncodedOrigin;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = swap ? height : width;
        var orientedHeight = swap ? width : height;
        var maxWidth = kind == "banner" ? 1600 : 512;
        var maxHeight = kind == "banner" ? 400 : 512;
        var scale = Math.Min(1d, Math.Min((double)maxWidth / orientedWidth, (double)maxHeight / orientedHeight));
        int outputWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        int outputHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));
        // A fresh bitmap receives pixels only: source EXIF/GPS/XMP/ICC/text never reaches output.
        using var output = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(output))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)outputWidth / orientedWidth, (float)outputHeight / orientedHeight);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(decoded, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        using var encoded = output.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null || encoded.Size > MaxOutputBytes) throw Invalid();
        return new(encoded.ToArray(), outputWidth, outputHeight);
    }

    private static MediaValidationException Invalid() => new("Choose a complete, still PNG or JPEG image of at most 5 MiB.");

    private static void ValidateContainer(ReadOnlySpan<byte> input)
    {
        if (input.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            int position = 8, chunks = 0;
            while (position < input.Length)
            {
                if (++chunks > 10_000 || input.Length - position < 12) throw Invalid();
                uint length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(position, 4));
                if (length > input.Length - position - 12) throw Invalid();
                var type = input.Slice(position + 4, 4);
                uint crc = uint.MaxValue;
                foreach (var b in input.Slice(position + 4, checked((int)length + 4))) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
                if (~crc != BinaryPrimitives.ReadUInt32BigEndian(input.Slice(position + 8 + (int)length, 4))) throw Invalid();
                if (type.SequenceEqual("acTL"u8)) throw Invalid();
                position += checked((int)length + 12);
                if (type.SequenceEqual("IEND"u8))
                {
                    if (length != 0 || position != input.Length) throw Invalid();
                    return;
                }
            }
            throw Invalid();
        }
        if (input.Length < 4 || input[0] != 255 || input[1] != 216 || input[^2] != 255 || input[^1] != 217)
            throw Invalid();
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint crc = index;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            table[index] = crc;
        }
        return table;
    }
}
