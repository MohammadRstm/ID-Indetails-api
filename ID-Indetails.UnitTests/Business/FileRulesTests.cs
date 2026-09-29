using System.Text;
using ID_Indetails.api.Business.Files;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class FileRulesTests
{
    private static byte[] Header(params byte[] start) => [.. start, .. new byte[FileRules.HeaderLength]];

    private static byte[] IsoMedia(string brand) => [0, 0, 0, 0x18, .. Encoding.ASCII.GetBytes("ftyp" + brand), 0, 0];

    [Fact]
    public void Photos_and_videos_are_recognised_from_their_first_bytes()
    {
        FileRules.Detect(Header(0xFF, 0xD8, 0xFF, 0xE0))!.MimeType.ShouldBe("image/jpeg");
        FileRules.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))!.MimeType.ShouldBe("image/png");
        FileRules.Detect([.. Encoding.ASCII.GetBytes("RIFF"), 1, 2, 3, 4, .. Encoding.ASCII.GetBytes("WEBP")])!.MimeType.ShouldBe("image/webp");
        FileRules.Detect(IsoMedia("heic"))!.ShouldBe(new FileRules.Detected(FileKind.Image, "image/heic", ".heic"));
        FileRules.Detect(IsoMedia("isom"))!.ShouldBe(new FileRules.Detected(FileKind.Video, "video/mp4", ".mp4"));
        FileRules.Detect(IsoMedia("qt  "))!.MimeType.ShouldBe("video/quicktime");
        FileRules.Detect(Header(0x1A, 0x45, 0xDF, 0xA3))!.Kind.ShouldBe(FileKind.Video);
    }

    [Fact]
    public void Anything_else_is_refused_whatever_it_is_called()
    {
        FileRules.Detect(Encoding.ASCII.GetBytes("%PDF-1.7 hello world")).ShouldBeNull();
        FileRules.Detect(Encoding.ASCII.GetBytes("<svg xmlns=\"http")).ShouldBeNull();
        FileRules.Detect(IsoMedia("zzzz")).ShouldBeNull();
        FileRules.Detect([0xFF, 0xD8]).ShouldBeNull();
    }

    [Fact]
    public void Photos_go_up_to_10_MB_and_videos_up_to_100_MB()
    {
        FileRules.CheckSize(FileKind.Image, FileRules.MaxImageBytes).ShouldBeNull();
        FileRules.CheckSize(FileKind.Image, FileRules.MaxImageBytes + 1)!.Code.ShouldBe(ErrorCodes.FileTooLarge);
        FileRules.CheckSize(FileKind.Video, FileRules.MaxImageBytes + 1).ShouldBeNull();
        FileRules.CheckSize(FileKind.Video, FileRules.MaxVideoBytes + 1)!.Code.ShouldBe(ErrorCodes.FileTooLarge);
    }

    [Fact]
    public void Names_lose_their_path_and_odd_characters_and_get_the_real_extension()
    {
        FileRules.SafeName("site photo.jpeg", ".jpg").ShouldBe("site photo.jpg");
        FileRules.SafeName("../../etc/passwd", ".png").ShouldBe("passwd.png");
        FileRules.SafeName(@"C:\Users\me\slab.exe", ".png").ShouldBe("slab.png");
        FileRules.SafeName("<script>.png", ".png").ShouldBe("script.png");
        FileRules.SafeName(null, ".mp4").ShouldBe("file.mp4");
        FileRules.SafeName(new string('a', 300), ".jpg").Length.ShouldBe(FileRules.MaxNameLength + 4);
    }
}
