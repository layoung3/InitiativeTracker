using SkiaSharp;

namespace InitiativeTracker.Web.Services;

public static class ImageSmokeTest
{
    // Published-artifact CI mode: deterministic pixels, no database, files, network or server startup.
    public static void Run()
    {
        using var source = new SKBitmap(1200, 600);
        source.Erase(SKColors.Blue);
        foreach (var format in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg })
        {
            using var encoded = source.Encode(format, 95);
            var result = new ImageNormalizer().Normalize(encoded.ToArray(), "portrait");
            using var output = SKBitmap.Decode(result.Bytes);
            if (output is null || output.Width != 512 || output.Height != 256 || output.GetPixel(100, 100).Blue < 240)
                throw new InvalidOperationException("Published native image decode/resize/encode failed.");
        }
        Console.WriteLine($"Published native PNG/JPEG decode, resize and PNG encode passed on {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}.");
    }
}
