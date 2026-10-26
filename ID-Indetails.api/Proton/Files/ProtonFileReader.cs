using System.IO.Compression;
using System.Text;
using System.Xml;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Files.Implementation;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ID_Indetails.api.Proton.Files;

/// <summary>What was read of a file, in words, with what the model looks at itself.</summary>
/// <param name="Read">What the attachments hold ("pages 1 to 20 of 54", "16 pictures, one every 7 seconds").</param>
/// <param name="More">How to read what was left out, when something was.</param>
public sealed record ProtonFileRead(string Kind, string Read, string? More, IReadOnlyList<ProtonAttachment> Attachments);

/// <summary>
/// Turns a project file into what the model can read: pictures (made small), a PDF's pages (a range at a time), the
/// text of Word, Excel, PowerPoint and plain text files, and pictures taken through a video (no sound). Anything else,
/// and anything that can't be read safely, is refused with <see cref="ProtonToolInputException"/>.
/// </summary>
public sealed class ProtonFileReader(IOptions<ProtonOptions> options, ProtonVideoFrames video)
{
    /// <summary>The longest side of a picture the model gets: larger ones cost more and aren't read better.</summary>
    public const int ImageEdge = 1568;

    /// <summary>The picture types the model reads as they are.</summary>
    private static readonly HashSet<string> ModelImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".tsv", ".md", ".json", ".xml", ".svg", ".log", ".rtf", ".html", ".htm",
    };

    private ProtonFileOptions Limits => options.Value.Files;

    /// <summary>What kind of file this is for reading, or null when it can't be read.</summary>
    public static string? KindOf(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "pdf",
            ".docx" => "word",
            ".xlsx" => "excel",
            ".pptx" => "powerpoint",
            ".mp4" or ".m4v" or ".mov" or ".webm" or ".mkv" or ".avi" => "video",
            _ when TextExtensions.Contains(extension) => "text",
            _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) => "pdf",
            _ when contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => "text",
            _ => null,
        };
    }

    /// <param name="fromPage">The first PDF page wanted, from 1.</param>
    public async Task<ProtonFileRead> ReadAsync(ProjectFileBytes file, int? fromPage, CancellationToken cancellationToken)
    {
        var kind = KindOf(file.FileName, file.ContentType)
            ?? throw new ProtonToolInputException($"{file.FileName} is a kind of file Proton can't read: only PDFs, pictures, videos, Word, Excel, PowerPoint and text files.");
        var data = file.Data ?? throw new ProtonToolInputException(
            $"{file.FileName} is {file.Size / 1_000_000.0:0.#} MB: Proton reads files up to {Limits.MaxBytes / 1_000_000} MB.");
        var label = $"The content of the project file \"{file.Title}\" ({file.FileName})";
        try
        {
            return kind switch
            {
                "image" => Image(data, file.FileName, file.ContentType, label),
                "pdf" => Pdf(data, file.FileName, fromPage ?? 1, label),
                "word" => Text("word", Office.Word(data), label),
                "excel" => Text("excel", Office.Excel(data), label),
                "powerpoint" => Text("powerpoint", Office.PowerPoint(data), label),
                "text" => Text("text", Decode(data), label),
                _ => await VideoAsync(data, file.FileName, label, cancellationToken),
            };
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidDataException)
        {
            // a damaged part, or one that declares what it must not (a DTD, an entity)
            throw new ProtonToolInputException($"{file.FileName} couldn't be read: it may be damaged.");
        }
    }

    private static ProtonFileRead Image(byte[] data, string fileName, string contentType, string label)
    {
        if (Pictures.PixelsOf(data) is not { } pixels || pixels > Pictures.MaxPixels)
        {
            throw new ProtonToolInputException($"{fileName} can't be read as a picture here.");
        }

        var shrunk = Pictures.Shrink(data, ImageEdge);
        var mediaType = shrunk is null ? contentType.ToLowerInvariant() : Pictures.WebpMimeType;
        if (!ModelImages.Contains(mediaType))
        {
            throw new ProtonToolInputException($"{fileName} is a kind of picture Proton can't read.");
        }

        return new ProtonFileRead("image", "the picture", null, [new ProtonAttachment(ProtonAttachmentKind.Image, mediaType, shrunk ?? data, label)]);
    }

    private ProtonFileRead Pdf(byte[] data, string fileName, int fromPage, string label)
    {
        PdfDocument source;
        try
        {
            source = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Import);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // protected or damaged: only a short one is sent as it is, for the model to try
            return data.Length <= 5_000_000
                ? new ProtonFileRead("pdf", "the whole PDF (its pages couldn't be counted)", null, [new ProtonAttachment(ProtonAttachmentKind.Pdf, "application/pdf", data, label)])
                : throw new ProtonToolInputException($"{fileName} couldn't be opened: it may be protected or damaged.");
        }

        using (source)
        {
            var pages = source.PageCount;
            if (pages == 0)
            {
                throw new ProtonToolInputException($"{fileName} has no pages.");
            }

            if (fromPage < 1 || fromPage > pages)
            {
                throw new ProtonToolInputException($"{fileName} has {pages} pages: start from 1 to {pages}.");
            }

            var last = Math.Min(pages, fromPage + Limits.MaxPdfPages - 1);
            var more = last < pages ? $"Pages {last + 1} to {pages} weren't read: call again with from_page {last + 1}." : null;
            if (fromPage == 1 && last == pages)
            {
                return new ProtonFileRead("pdf", $"all {pages} pages", null, [new ProtonAttachment(ProtonAttachmentKind.Pdf, "application/pdf", data, label)]);
            }

            using var part = new PdfDocument();
            for (var page = fromPage; page <= last; page++)
            {
                part.AddPage(source.Pages[page - 1]);
            }

            using var saved = new MemoryStream();
            part.Save(saved, false);
            return new ProtonFileRead("pdf", $"pages {fromPage} to {last} of {pages}", more,
                [new ProtonAttachment(ProtonAttachmentKind.Pdf, "application/pdf", saved.ToArray(), $"{label}, pages {fromPage} to {last}")]);
        }
    }

    private ProtonFileRead Text(string kind, string text, string label)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return new ProtonFileRead(kind, "no text: the file is empty, or holds only pictures", null, []);
        }

        var cut = text.Length > Limits.MaxTextCharacters;
        var kept = cut ? text[..Limits.MaxTextCharacters] : text;
        return new ProtonFileRead(kind, cut ? $"the first {kept.Length:N0} of {text.Length:N0} characters" : $"all {text.Length:N0} characters of its text",
            cut ? "The rest of the text wasn't read: say so." : null,
            [new ProtonAttachment(ProtonAttachmentKind.Text, "text/plain", Encoding.UTF8.GetBytes(kept), label)]);
    }

    private async Task<ProtonFileRead> VideoAsync(byte[] data, string fileName, string label, CancellationToken cancellationToken)
    {
        var frames = await video.TakeAsync(data, fileName, Limits.MaxVideoFrames, cancellationToken);
        var every = frames.Duration is { } seconds && frames.Pictures.Count > 1 ? $", one every {seconds / frames.Pictures.Count:0.#} seconds" : "";
        return new ProtonFileRead("video", $"{frames.Pictures.Count} pictures taken through the video{every}; its sound isn't read", null,
            frames.Pictures.Select((picture, i) => new ProtonAttachment(ProtonAttachmentKind.Image, "image/jpeg", picture, $"{label}, picture {i + 1} of {frames.Pictures.Count}")).ToList());
    }

    /// <summary>UTF-8 (with or without its mark), UTF-16 with its mark; invalid bytes become replacement characters.</summary>
    private static string Decode(byte[] data)
    {
        using var reader = new StreamReader(new MemoryStream(data), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The text of Office Open XML files, read from their parts with nothing else run: paragraphs on lines, table cells
    /// and spreadsheet cells separated by tabs. Older binary files (.doc, .xls, .ppt) aren't read.
    /// </summary>
    internal static class Office
    {
        /// <summary>The most rows read of each sheet: past this, a spreadsheet is data for a report, not for reading.</summary>
        private const int MaxRows = 2_000;

        /// <summary>
        /// The most text collected from one file, well above what the model is given: a small file that unzips to a huge
        /// one (a zip bomb) stops here instead of filling the server's memory.
        /// </summary>
        private const int MaxCollected = 1_000_000;

        public static string Word(byte[] data)
        {
            using var zip = Open(data);
            return TextOf(zip.GetEntry("word/document.xml"), paragraph: "p", text: "t", tab: "tab", cell: "tc");
        }

        public static string PowerPoint(byte[] data)
        {
            using var zip = Open(data);
            var slides = zip.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .OrderBy(e => int.TryParse(e.FullName["ppt/slides/slide".Length..^4], out var n) ? n : int.MaxValue)
                .ToList();
            var text = new StringBuilder();
            for (var i = 0; i < slides.Count && text.Length < MaxCollected; i++)
            {
                text.Append("Slide ").Append(i + 1).Append(":\n").Append(TextOf(slides[i], paragraph: "p", text: "t", tab: null, cell: "tc").Trim()).Append("\n\n");
            }

            return text.ToString();
        }

        public static string Excel(byte[] data)
        {
            using var zip = Open(data);
            var shared = SharedStrings(zip.GetEntry("xl/sharedStrings.xml"));
            var sheets = zip.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .OrderBy(e => int.TryParse(e.FullName["xl/worksheets/sheet".Length..^4], out var n) ? n : int.MaxValue)
                .ToList();
            var text = new StringBuilder();
            for (var i = 0; i < sheets.Count && text.Length < MaxCollected; i++)
            {
                text.Append("Sheet ").Append(i + 1).Append(":\n");
                using var reader = XmlOf(sheets[i]);
                var row = new List<string>();
                var rows = 0;
                string? type = null;
                while (reader.Read() && rows < MaxRows && text.Length < MaxCollected)
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
                    {
                        type = reader.GetAttribute("t");
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "v" or "t")
                    {
                        var value = reader.ReadElementContentAsString();
                        row.Add(type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? shared[index] : value);
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
                    {
                        if (row.Count > 0)
                        {
                            text.Append(string.Join('\t', row)).Append('\n');
                            rows++;
                        }

                        row.Clear();
                    }
                }

                if (rows == MaxRows)
                {
                    text.Append($"(only the first {MaxRows} rows were read)\n");
                }

                text.Append('\n');
            }

            return text.ToString();
        }

        private static ZipArchive Open(byte[] data)
        {
            try
            {
                return new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                throw new ProtonToolInputException("The file couldn't be opened: it may be damaged, or an older format (.doc, .xls, .ppt) Proton can't read.");
            }
        }

        private static List<string> SharedStrings(ZipArchiveEntry? entry)
        {
            var strings = new List<string>();
            if (entry is null)
            {
                return strings;
            }

            using var reader = XmlOf(entry);
            var current = new StringBuilder();
            var total = 0;
            while (reader.Read() && total < MaxCollected)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                {
                    current.Append(reader.ReadElementContentAsString());
                }

                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si")
                {
                    strings.Add(current.ToString());
                    total += current.Length;
                    current.Clear();
                }
            }

            return strings;
        }

        private static string TextOf(ZipArchiveEntry? entry, string paragraph, string text, string? tab, string cell)
        {
            if (entry is null)
            {
                return string.Empty;
            }

            using var reader = XmlOf(entry);
            var result = new StringBuilder();
            while (reader.Read() && result.Length < MaxCollected)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == text)
                {
                    result.Append(reader.ReadElementContentAsString());
                    continue;
                }

                if (reader.NodeType == XmlNodeType.Element && (reader.LocalName == tab || reader.LocalName == "br"))
                {
                    result.Append(reader.LocalName == "br" ? '\n' : '\t');
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == paragraph)
                {
                    result.Append('\n');
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == cell)
                {
                    result.Append('\t');
                }
            }

            return result.ToString();
        }

        /// <summary>An XML part, read without DTDs or external entities, and only up to a size a document's text needs.</summary>
        private static XmlReader XmlOf(ZipArchiveEntry entry)
            => XmlReader.Create(entry.Open(), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 50_000_000,
                IgnoreWhitespace = false,
            });
    }
}
