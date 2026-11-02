using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using SkiaSharp;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// The company's mark behind every page of what goes to a client: the split "0" of the logo, in pale green, coming in
/// from the page's edges and fading out before the content. Half of it from the middle of the right edge, a quarter of
/// it in the bottom-left corner. MigraDoc can't fade a shape, so each part is drawn once, faded, as a transparent
/// picture, and placed on the page behind the text, which stays text.
/// </summary>
public static class PdfWatermark
{
    // The split 0 is drawn in units of a 448 × 832 box, shown 120 mm wide on an A4 page (210 × 297 mm).
    private const float BoxWidth = 448;
    private const float BoxHeight = 832;
    private const double WidthMm = 120;
    private const double HeightMm = WidthMm * BoxHeight / BoxWidth;
    private const double PageWidthMm = 210;
    private const double PageHeightMm = 297;

    /// <summary>How far from the page edge it has faded out: strongest at the edge, gone well before the text.</summary>
    private const double FadeMm = 50.4;
    private const float Opacity = 0.16f;
    private const float PixelsPerMm = 8;
    private static readonly SKColor Green = new(0x1F, 0x7A, 0x5A);

    /// <summary>The left half of the mark, at the middle of the right edge.</summary>
    private static readonly Lazy<string> RightHalf = new(() => Picture(
        widthMm: WidthMm / 2, heightMm: HeightMm, logoLeftMm: 0, logoTopMm: 0, edgeOnRight: true));

    /// <summary>The top-right quarter of the mark, its centre on the bottom-left corner.</summary>
    private static readonly Lazy<string> CornerQuarter = new(() => Picture(
        widthMm: WidthMm / 2, heightMm: HeightMm / 2, logoLeftMm: -WidthMm / 2, logoTopMm: 0, edgeOnRight: false));

    /// <summary>Puts the mark behind every page of the section.</summary>
    public static void AddTo(Section section)
    {
        var header = section.Headers.Primary;
        Place(header, RightHalf.Value, PageWidthMm - WidthMm / 2, (PageHeightMm - HeightMm) / 2, WidthMm / 2, HeightMm);
        Place(header, CornerQuarter.Value, 0, PageHeightMm - HeightMm / 2, WidthMm / 2, HeightMm / 2);
    }

    private static void Place(HeaderFooter header, string picture, double leftMm, double topMm, double widthMm, double heightMm)
    {
        var image = header.AddImage(picture);
        image.LockAspectRatio = false;
        image.Width = Unit.FromMillimeter(widthMm);
        image.Height = Unit.FromMillimeter(heightMm);
        image.RelativeHorizontal = RelativeHorizontal.Page;
        image.RelativeVertical = RelativeVertical.Page;
        image.Left = Unit.FromMillimeter(leftMm);
        image.Top = Unit.FromMillimeter(topMm);
        // behind the text, and taking no room from it
        image.WrapFormat.Style = WrapStyle.Through;
    }

    /// <summary>
    /// Part of the mark as a transparent PNG (MigraDoc's "base64:" source): the logo drawn at the given offset, cut to
    /// the picture, faded from the page edge (the picture's right side, or its left) inwards.
    /// </summary>
    private static string Picture(double widthMm, double heightMm, double logoLeftMm, double logoTopMm, bool edgeOnRight)
    {
        var width = (int)Math.Round(widthMm * PixelsPerMm);
        var height = (int)Math.Round(heightMm * PixelsPerMm);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var edge = edgeOnRight ? width : 0f;
        var faded = edgeOnRight ? width - (float)(FadeMm * PixelsPerMm) : (float)(FadeMm * PixelsPerMm);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(edge, 0),
                new SKPoint(faded, 0),
                [Green.WithAlpha((byte)(255 * Opacity)), Green.WithAlpha(0)],
                SKShaderTileMode.Clamp),
        };

        var scale = (float)(WidthMm * PixelsPerMm / BoxWidth);
        canvas.Translate((float)(logoLeftMm * PixelsPerMm), (float)(logoTopMm * PixelsPerMm));
        canvas.Scale(scale);
        using var mark = SplitZero();
        canvas.DrawPath(mark, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return "base64:" + Convert.ToBase64String(png.ToArray());
    }

    /// <summary>The logo's split 0: a tall stadium with a rounded slot in it, cut through at the top and the bottom.</summary>
    private static SKPath SplitZero()
    {
        using var outer = SKPath.ParseSvgPathData("M224 0 A224 224 0 0 1 448 224 V608 A224 224 0 0 1 224 832 A224 224 0 0 1 0 608 V224 A224 224 0 0 1 224 0 Z");
        using var slot = SKPath.ParseSvgPathData("M216 150 H232 A70 70 0 0 1 302 220 V612 A70 70 0 0 1 232 682 H216 A70 70 0 0 1 146 612 V220 A70 70 0 0 1 216 150 Z");
        using var cuts = SKPath.ParseSvgPathData("M208 -1 H240 V160 H208 Z M208 672 H240 V833 H208 Z");
        using var ring = outer.Op(slot, SKPathOp.Difference);
        return ring.Op(cuts, SKPathOp.Difference);
    }
}
