using ID_Indetails.api.Services.Files.Implementation;
using Shouldly;
using SkiaSharp;

namespace ID_Indetails.UnitTests.Business;

public class PicturesTests
{
    /// <summary>A photo-like picture of the given size, as PNG.</summary>
    private static byte[] Photo(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            // a sky, a hill and a few windows: smooth like a photo, so it compresses like one
            using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), [new SKColor(90, 140, 210), new SKColor(240, 200, 150)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(0, 0, width, height, sky);
            using var hill = new SKPaint { Color = new SKColor(60, 110, 70), IsAntialias = true };
            canvas.DrawOval(width / 2f, height, width * 0.7f, height * 0.45f, hill);
            using var window = new SKPaint { Color = new SKColor(250, 240, 210), IsAntialias = true };
            for (var i = 0; i < 12; i++)
            {
                canvas.DrawRect(width * (0.1f + i * 0.07f), height * 0.55f, width * 0.04f, height * 0.06f, window);
            }
        }

        using var png = bitmap.PeekPixels().Encode(SKEncodedImageFormat.Png, 100)!;
        return png.ToArray();
    }

    private static SKSizeI SizeOf(byte[] picture)
    {
        using var codec = SKCodec.Create(new MemoryStream(picture));
        return codec!.Info.Size;
    }

    [Fact]
    public void A_big_photo_is_made_no_bigger_than_the_longest_side_kept_and_much_lighter()
    {
        var photo = Photo(3200, 2000);

        var small = Pictures.Shrink(photo, Pictures.MaxEdge).ShouldNotBeNull();

        SizeOf(small).ShouldBe(new SKSizeI(1920, 1200));
        small.Length.ShouldBeLessThan(photo.Length / 4);
        // WebP's signature
        System.Text.Encoding.ASCII.GetString(small, 8, 4).ShouldBe("WEBP");
    }

    [Fact]
    public void A_smaller_copy_keeps_the_photo_s_shape()
    {
        SizeOf(Pictures.Shrink(Photo(1200, 1600), 480).ShouldNotBeNull()).ShouldBe(new SKSizeI(360, 480));
    }

    [Fact]
    public void What_isn_t_a_readable_photo_is_left_as_it_is()
        => Pictures.Shrink("not a picture at all"u8.ToArray(), Pictures.MaxEdge).ShouldBeNull();

    [Theory]
    [InlineData(1, 160)]
    [InlineData(160, 160)]
    [InlineData(200, 480)]
    [InlineData(1000, 1920)]
    [InlineData(5000, 1920)]
    public void A_size_asked_for_is_rounded_up_to_one_kept(int wanted, int kept) => Pictures.SizeFor(wanted).ShouldBe(kept);
}
