using SkiaSharp;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// Photos made small: turned upright (as the camera held them), no bigger than they are shown, and saved as WebP. A
/// phone photo of several MB becomes a few hundred KB. A photo SkiaSharp can't read (HEIC on Linux) is left as it is.
/// </summary>
public static class Pictures
{
    /// <summary>
    /// The longest side kept of an uploaded photo: a full-HD screen's width. The website shows a page picture at most
    /// about 1200 px wide; a photo is never cropped, since its card is shown at two shapes and the gallery shows it whole.
    /// </summary>
    public const int MaxEdge = 1920;

    /// <summary>The smaller copies served on request (<c>?size=</c>), each its longest side.</summary>
    public static readonly IReadOnlyList<int> Sizes = [160, 480, 960, 1920];

    public const string WebpExtension = ".webp";
    public const string WebpMimeType = "image/webp";

    /// <summary>
    /// WebP at 75 looks the same as the original on screen at a fraction of its size; the smaller copies, seen small, at 70.
    /// </summary>
    private const int Quality = 75;
    private const int CopyQuality = 70;

    /// <summary>The smallest of <see cref="Sizes"/> at least <paramref name="wanted"/>; the largest when none is.</summary>
    public static int SizeFor(int wanted) => Sizes.FirstOrDefault(s => s >= wanted, Sizes[^1]);

    /// <summary>
    /// The photo upright, its longest side at most <paramref name="maxEdge"/>, as WebP. Null when it can't be read, or
    /// when it is already that small and the WebP isn't smaller than <paramref name="photo"/>: the original is kept then.
    /// <paramref name="mark"/> changes the pixels before they are saved (the <see cref="Watermark"/>); a marked photo
    /// always comes back as a new WebP.
    /// </summary>
    public static byte[]? Shrink(byte[] photo, int maxEdge, Action<SKBitmap>? mark = null)
    {
        using var codec = SKCodec.Create(new MemoryStream(photo));
        using var decoded = codec is null ? null : SKBitmap.Decode(codec);
        if (codec is null || decoded is null)
        {
            return null;
        }

        using var turned = Upright(decoded, codec.EncodedOrigin);
        var upright = turned ?? decoded;
        var longest = Math.Max(upright.Width, upright.Height);
        using var resized = longest > maxEdge
            ? upright.Resize(new SKSizeI(Scaled(upright.Width, maxEdge, longest), Scaled(upright.Height, maxEdge, longest)), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var shown = resized ?? upright;
        mark?.Invoke(shown);
        using var pixels = shown.PeekPixels();
        using var webp = pixels.Encode(SKEncodedImageFormat.Webp, maxEdge < MaxEdge ? CopyQuality : Quality);
        if (webp is null)
        {
            return null;
        }

        // a photo already small and already compressed may not gain anything: keep it, then
        return mark is null && resized is null && turned is null && webp.Size >= photo.Length ? null : webp.ToArray();
    }

    private static int Scaled(int side, int maxEdge, int longest) => Math.Max(1, (int)Math.Round(side * (double)maxEdge / longest));

    /// <summary>The photo turned as its camera says it was held; null when it already is.</summary>
    private static SKBitmap? Upright(SKBitmap photo, SKEncodedOrigin origin)
    {
        var degrees = origin switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (degrees == 0)
        {
            return null;
        }

        var sideways = degrees != 180;
        var turned = new SKBitmap(sideways ? photo.Height : photo.Width, sideways ? photo.Width : photo.Height);
        using var canvas = new SKCanvas(turned);
        canvas.Translate(turned.Width / 2f, turned.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-photo.Width / 2f, -photo.Height / 2f);
        canvas.DrawBitmap(photo, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return turned;
    }
}
