using ID_Indetails.api.Services.Files.Implementation;
using Shouldly;
using SkiaSharp;

namespace ID_Indetails.UnitTests.Business;

public class WatermarkTests
{
    private const string Key = "a-secret-watermark-key";

    /// <summary>A photo-like picture: a sky, a hill and a few windows.</summary>
    private static SKBitmap Photo(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), [new SKColor(90, 140, 210), new SKColor(240, 200, 150)], SKShaderTileMode.Clamp) };
        canvas.DrawRect(0, 0, width, height, sky);
        using var hill = new SKPaint { Color = new SKColor(60, 110, 70), IsAntialias = true };
        canvas.DrawOval(width / 2f, height, width * 0.7f, height * 0.45f, hill);
        using var window = new SKPaint { Color = new SKColor(250, 240, 210), IsAntialias = true };
        for (var i = 0; i < 12; i++)
        {
            canvas.DrawRect(width * (0.1f + i * 0.07f), height * 0.55f, width * 0.04f, height * 0.06f, window);
        }

        return bitmap;
    }

    /// <summary>The picture saved as WebP at the quality the website's copies get, read back, at another size.</summary>
    private static SKBitmap Resaved(SKBitmap picture, int width, int height, int quality = 70)
    {
        using var resized = picture.Resize(new SKSizeI(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var webp = resized.PeekPixels().Encode(SKEncodedImageFormat.Webp, quality);
        return SKBitmap.Decode(webp);
    }

    [Fact]
    public void A_marked_picture_says_which_it_is_after_being_resized_and_resaved()
    {
        using var picture = Photo(1920, 1200);
        Watermark.Embed(picture, 12_345, Key);

        foreach (var (width, height) in new[] { (1920, 1200), (960, 600), (480, 300) })
        {
            using var copy = Resaved(picture, width, height);
            var reading = Watermark.Read(copy, Key);
            reading.Found.ShouldBeTrue($"{width}×{height}");
            reading.PictureId.ShouldBe(12_345);
        }
    }

    [Fact]
    public void The_mark_is_not_seen()
    {
        using var original = Photo(960, 600);
        using var picture = original.Copy();
        Watermark.Embed(picture, 7, Key);

        var largest = 0;
        for (var y = 0; y < picture.Height; y += 3)
        {
            for (var x = 0; x < picture.Width; x += 3)
            {
                largest = Math.Max(largest, Math.Abs(picture.GetPixel(x, y).Green - original.GetPixel(x, y).Green));
            }
        }

        largest.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void A_picture_without_the_mark_or_read_with_another_key_says_nothing()
    {
        using var plain = Photo(960, 600);
        Watermark.Read(plain, Key).Found.ShouldBeFalse();

        Watermark.Embed(plain, 99, Key);
        Watermark.Read(plain, "another-key-entirely!").Found.ShouldBeFalse();
    }
}
