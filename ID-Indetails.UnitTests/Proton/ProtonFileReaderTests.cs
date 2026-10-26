using System.IO.Compression;
using System.Text;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Files;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Shouldly;
using SkiaSharp;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>What Proton can read of a project file, and what it refuses: by kind, by size, a PDF's pages at a time.</summary>
public class ProtonFileReaderTests
{
    private static ProtonFileReader Reader(ProtonFileOptions? files = null)
    {
        var options = Options.Create(new ProtonOptions { Files = files ?? new ProtonFileOptions { FfmpegPath = "" } });
        return new ProtonFileReader(options, new ProtonVideoFrames(options, NullLogger<ProtonVideoFrames>.Instance));
    }

    private static ProjectFileBytes File(string name, string type, byte[]? data) => new(7, "Floor plan", name, type, data?.Length ?? 30_000_000, data);

    private static Task<ProtonFileRead> ReadAsync(ProjectFileBytes file, int? fromPage = null, ProtonFileOptions? files = null)
        => Reader(files).ReadAsync(file, fromPage, CancellationToken.None);

    [Theory]
    [InlineData("plan.pdf", "application/pdf", "pdf")]
    [InlineData("site.JPG", "image/jpeg", "image")]
    [InlineData("walkthrough.mp4", "video/mp4", "video")]
    [InlineData("brief.docx", "application/octet-stream", "word")]
    [InlineData("costs.xlsx", "application/octet-stream", "excel")]
    [InlineData("pitch.pptx", "application/octet-stream", "powerpoint")]
    [InlineData("notes.txt", "text/plain", "text")]
    [InlineData("model.dwg", "application/acad", null)]
    [InlineData("house.skp", "application/octet-stream", null)]
    [InlineData("old.doc", "application/msword", null)]
    [InlineData("all.zip", "application/zip", null)]
    public void Knows_which_files_it_can_read(string name, string type, string? kind)
        => ProtonFileReader.KindOf(name, type).ShouldBe(kind);

    [Fact]
    public async Task Refuses_what_it_can_t_read_and_what_is_too_large()
    {
        (await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("model.dwg", "application/acad", [1, 2, 3]))))
            .Message.ShouldContain("can't read");
        // a file over the limit is never read: its bytes don't come back
        (await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("big.pdf", "application/pdf", null))))
            .Message.ShouldContain("30 MB");
    }

    [Fact]
    public async Task Reads_a_long_pdf_a_range_of_pages_at_a_time()
    {
        var pdf = Pdf(45);

        var first = await ReadAsync(File("plan.pdf", "application/pdf", pdf));
        first.Read.ShouldBe("pages 1 to 20 of 45");
        first.More!.ShouldContain("from_page 21");
        Pages(first.Attachments.Single().Data).ShouldBe(20);

        var last = await ReadAsync(File("plan.pdf", "application/pdf", pdf), fromPage: 41);
        last.Read.ShouldBe("pages 41 to 45 of 45");
        last.More.ShouldBeNull();
        Pages(last.Attachments.Single().Data).ShouldBe(5);

        (await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("plan.pdf", "application/pdf", pdf), fromPage: 46))).Message.ShouldContain("45 pages");

        // a short one goes whole, as it is
        var whole = await ReadAsync(File("short.pdf", "application/pdf", Pdf(3)));
        whole.Read.ShouldBe("all 3 pages");
        whole.Attachments.Single().Kind.ShouldBe(ProtonAttachmentKind.Pdf);
    }

    [Fact]
    public async Task Makes_a_large_picture_small_and_refuses_one_it_cannot_decode()
    {
        var read = await ReadAsync(File("site.png", "image/png", Png(3000, 2000)));

        var picture = read.Attachments.Single();
        picture.Kind.ShouldBe(ProtonAttachmentKind.Image);
        using var shown = SKBitmap.Decode(picture.Data);
        Math.Max(shown.Width, shown.Height).ShouldBe(ProtonFileReader.ImageEdge);

        await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("broken.jpg", "image/jpeg", [0xFF, 0xD8, 1, 2, 3])));
    }

    [Fact]
    public async Task Reads_the_text_of_word_excel_and_powerpoint_files()
    {
        var word = await ReadAsync(File("brief.docx", "application/octet-stream", Zip(("word/document.xml",
            """<w:document xmlns:w="w"><w:body><w:p><w:r><w:t>Client wants</w:t></w:r><w:r><w:t> oak floors.</w:t></w:r></w:p><w:p><w:r><w:t>Budget: tight</w:t></w:r></w:p></w:body></w:document>"""))));
        Text(word).ShouldBe("Client wants oak floors.\nBudget: tight");

        var excel = await ReadAsync(File("costs.xlsx", "application/octet-stream", Zip(
            ("xl/sharedStrings.xml", """<sst xmlns="x"><si><t>Item</t></si><si><t>Cost</t></si><si><t>Tiles</t></si></sst>"""),
            ("xl/worksheets/sheet1.xml", """<worksheet xmlns="x"><sheetData><row><c t="s"><v>0</v></c><c t="s"><v>1</v></c></row><row><c t="s"><v>2</v></c><c><v>1200</v></c></row></sheetData></worksheet>"""))));
        Text(excel).ShouldContain("Item\tCost\nTiles\t1200");

        var slides = await ReadAsync(File("pitch.pptx", "application/octet-stream", Zip(
            ("ppt/slides/slide2.xml", """<p:sld xmlns:p="p" xmlns:a="a"><a:p><a:r><a:t>Second</a:t></a:r></a:p></p:sld>"""),
            ("ppt/slides/slide1.xml", """<p:sld xmlns:p="p" xmlns:a="a"><a:p><a:r><a:t>First</a:t></a:r></a:p></p:sld>"""))));
        Text(slides).ShouldBe("Slide 1:\nFirst\n\nSlide 2:\nSecond");

        // an older binary Office file, or anything that isn't a zip, is refused
        await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("brief.docx", "application/octet-stream", [1, 2, 3])));
    }

    [Fact]
    public async Task Never_runs_what_a_document_declares_and_cuts_long_text()
    {
        // an external entity would read a file from the server: DTDs are refused outright
        var entity = Zip(("word/document.xml", """<!DOCTYPE d [<!ENTITY x SYSTEM "file:///etc/passwd">]><w:document xmlns:w="w"><w:p><w:t>&x;</w:t></w:p></w:document>"""));
        (await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("brief.docx", "application/octet-stream", entity)))).Message.ShouldContain("couldn't be read");

        var read = await ReadAsync(File("notes.txt", "text/plain", Encoding.UTF8.GetBytes(new string('a', 5_000))), files: new ProtonFileOptions { MaxTextCharacters = 1_000, FfmpegPath = "" });
        read.Read.ShouldBe("the first 1,000 of 5,000 characters");
        Text(read).Length.ShouldBe(1_000);
    }

    [Fact]
    public async Task Reads_a_video_only_when_the_server_can_and_only_real_videos()
    {
        ProtonVideoFrames.FormatOf([0, 0, 0, 0x18, .. "ftypmp42"u8]).ShouldBe("mov");
        ProtonVideoFrames.FormatOf([0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0]).ShouldBe("matroska");
        ProtonVideoFrames.FormatOf([.. "RIFF"u8, 0, 0, 0, 0, .. "AVI "u8]).ShouldBe("avi");
        // a playlist that would point ffmpeg at other files is no video
        ProtonVideoFrames.FormatOf("#EXTM3U\n#EXT-X-VERSION:3\n"u8).ShouldBeNull();

        // without ffmpeg configured, it says so
        (await Should.ThrowAsync<ProtonToolInputException>(ReadAsync(File("walk.mp4", "video/mp4", [0, 0, 0, 0x18, .. "ftypmp42"u8]))))
            .Message.ShouldContain("isn't set up");
    }

    /// <summary>Takes pictures through a real video. Needs ffmpeg (GitHub's Ubuntu runners have it): skipped without it.</summary>
    [FfmpegFact]
    public async Task Takes_pictures_through_a_video_and_none_of_its_sound()
    {
        var folder = Directory.CreateTempSubdirectory("proton-test-video-");
        try
        {
            var path = Path.Combine(folder.FullName, "clip.mp4");
            var made = System.Diagnostics.Process.Start("ffmpeg", ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=8:size=1280x720:rate=10", "-pix_fmt", "yuv420p", path])!;
            await made.WaitForExitAsync();
            var video = await System.IO.File.ReadAllBytesAsync(path);

            var read = await ReadAsync(File("clip.mp4", "video/mp4", video), files: new ProtonFileOptions { MaxVideoFrames = 4 });

            read.Attachments.Count.ShouldBe(4);
            read.Attachments.ShouldAllBe(a => a.Kind == ProtonAttachmentKind.Image && a.MediaType == "image/jpeg");
            read.Read.ShouldContain("one every 2 seconds");
            read.Read.ShouldContain("sound isn't read");
            using var first = SKBitmap.Decode(read.Attachments[0].Data);
            first.Width.ShouldBeLessThanOrEqualTo(768);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static string Text(ProtonFileRead read) => Encoding.UTF8.GetString(read.Attachments.Single(a => a.Kind == ProtonAttachmentKind.Text).Data).Trim();

    private static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static int Pages(byte[] pdf) => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import).PageCount;

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static byte[] Zip(params (string Name, string Xml)[] parts)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(xml);
            }
        }

        return stream.ToArray();
    }
}

/// <summary>A test that needs ffmpeg on the PATH: skipped, saying so, where it isn't installed.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        var found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => System.IO.File.Exists(Path.Combine(dir, "ffmpeg")) || System.IO.File.Exists(Path.Combine(dir, "ffmpeg.exe")));
        if (!found)
        {
            Skip = "Needs ffmpeg on the PATH.";
        }
    }
}
