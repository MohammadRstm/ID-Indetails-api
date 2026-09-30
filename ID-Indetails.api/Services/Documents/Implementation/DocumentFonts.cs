using PdfSharp.Fonts;
using PdfSharp.WPFonts;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// Every font a document asks for is Segoe WP, which ships inside PDFsharp, so PDFs look the same on any server
/// without installing fonts. Italic is simulated.
/// </summary>
public sealed class DocumentFonts : IFontResolver
{
    public const string Family = "Segoe WP";

    private const string Regular = "SegoeWP";
    private const string Bold = "SegoeWP-Bold";

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(isBold ? Bold : Regular, false, isItalic);

    public byte[] GetFont(string faceName)
        => faceName == Bold ? FontDataHelper.SegoeWPBold : FontDataHelper.SegoeWP;
}
