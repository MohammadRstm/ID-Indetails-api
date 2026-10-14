using System.Text;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PortalContentRulesTests
{
    private const long MiB = 1024 * 1024;

    private static byte[] Bytes(params byte[] start) => [.. start, .. new byte[64]];

    private static byte[] Text(string start) => Encoding.ASCII.GetBytes(start);

    [Theory]
    [InlineData("Living room.JPG", "image/jpeg", PortalPreview.Image)]
    [InlineData("walkthrough.mp4", "video/mp4", PortalPreview.Video)]
    [InlineData("site.mov", "video/quicktime", PortalPreview.None)]
    [InlineData("Plans rev B.pdf", "application/pdf", PortalPreview.Pdf)]
    [InlineData("ground floor.dwg", "image/vnd.dwg", PortalPreview.None)]
    [InlineData("chair.glb", "model/gltf-binary", PortalPreview.None)]
    public void A_file_is_known_by_its_extension(string name, string contentType, PortalPreview preview)
    {
        var type = PortalContentRules.TypeOf(name).ShouldNotBeNull();

        type.ContentType.ShouldBe(contentType);
        type.Preview.ShouldBe(preview);
    }

    [Theory]
    [InlineData("page.html")]
    [InlineData("logo.svg")]
    [InlineData("setup.exe")]
    [InlineData("script.js")]
    [InlineData("no-extension")]
    public void Web_pages_programs_and_unknown_files_are_not_taken(string name)
        => PortalContentRules.TypeOf(name).ShouldBeNull();

    [Fact]
    public void The_accepted_extensions_are_lower_case_without_dots_and_never_risky()
    {
        PortalContentRules.AcceptedExtensions.ShouldAllBe(e => e == e.ToLowerInvariant() && !e.StartsWith('.'));
        PortalContentRules.AcceptedExtensions.ShouldNotContain("svg");
        PortalContentRules.AcceptedExtensions.ShouldNotContain("html");
        PortalContentRules.AcceptedExtensions.Distinct().Count().ShouldBe(PortalContentRules.AcceptedExtensions.Count);
    }

    [Fact]
    public void The_first_bytes_must_fit_the_extension()
    {
        var jpg = PortalContentRules.TypeOf("a.jpg")!;
        PortalContentRules.MatchesContent(jpg, Bytes(0xFF, 0xD8, 0xFF, 0xE0)).ShouldBeTrue();
        PortalContentRules.MatchesContent(jpg, Bytes(0x89, 0x50, 0x4E, 0x47)).ShouldBeFalse();

        var pdf = PortalContentRules.TypeOf("a.pdf")!;
        PortalContentRules.MatchesContent(pdf, Text("%PDF-1.7\n...")).ShouldBeTrue();
        PortalContentRules.MatchesContent(pdf, Text("hello")).ShouldBeFalse();

        var mp4 = PortalContentRules.TypeOf("a.mp4")!;
        PortalContentRules.MatchesContent(mp4, Bytes(0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m')).ShouldBeTrue();

        var dwg = PortalContentRules.TypeOf("a.dwg")!;
        PortalContentRules.MatchesContent(dwg, Text("AC1032....")).ShouldBeTrue();
        PortalContentRules.MatchesContent(dwg, Text("PK....")).ShouldBeFalse();

        var docx = PortalContentRules.TypeOf("a.docx")!;
        PortalContentRules.MatchesContent(docx, Bytes(0x50, 0x4B, 0x03, 0x04)).ShouldBeTrue();
    }

    [Fact]
    public void Text_formats_must_be_text_and_never_a_web_page()
    {
        var obj = PortalContentRules.TypeOf("a.obj")!;
        PortalContentRules.MatchesContent(obj, Text("# Blender\nv 0 0 0\n")).ShouldBeTrue();
        PortalContentRules.MatchesContent(obj, Bytes(0x00, 0x01, 0x02)).ShouldBeFalse();
        PortalContentRules.MatchesContent(obj, Text("<!DOCTYPE html><html>")).ShouldBeFalse();
        PortalContentRules.MatchesContent(obj, Text("  <svg xmlns=\"http://www.w3.org/2000/svg\">")).ShouldBeFalse();
    }

    [Fact]
    public void A_program_is_refused_whatever_its_extension()
    {
        var stl = PortalContentRules.TypeOf("a.stl")!;
        PortalContentRules.MatchesContent(stl, Bytes(0x01, 0x02)).ShouldBeTrue();
        PortalContentRules.MatchesContent(stl, Text("MZ\u0090\0")).ShouldBeFalse();
        PortalContentRules.MatchesContent(stl, Bytes(0x7F, 0x45, 0x4C, 0x46)).ShouldBeFalse();
        PortalContentRules.MatchesContent(stl, []).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1L, 16 * MiB, 1)]
    [InlineData(16 * MiB, 16 * MiB, 1)]
    [InlineData(16 * MiB + 1, 16 * MiB, 2)]
    [InlineData(1024 * MiB, 16 * MiB, 64)]
    public void Parts_are_16_MiB_for_files_up_to_160_GiB(long size, long partSize, int parts)
    {
        PortalContentRules.PartSizeFor(size).ShouldBe(partSize);
        PortalContentRules.PartCountFor(size, partSize).ShouldBe(parts);
    }

    [Fact]
    public void A_huge_file_gets_bigger_parts_and_never_more_than_10000()
    {
        var size = 500L * 1024 * MiB; // 500 GiB
        var partSize = PortalContentRules.PartSizeFor(size);

        (partSize % MiB).ShouldBe(0);
        partSize.ShouldBeGreaterThan(16 * MiB);
        PortalContentRules.PartCountFor(size, partSize).ShouldBeLessThanOrEqualTo(PortalContentRules.MaxParts);
    }

    [Fact]
    public void An_upload_is_whole_only_when_every_part_has_its_size()
    {
        var size = (2 * 16 * MiB) + 100;
        var part = 16 * MiB;

        PortalContentRules.PartsNotReceived([(1, part), (2, part), (3, 100)], size, part).ShouldBeEmpty();
        PortalContentRules.PartsNotReceived([(1, part), (3, 100)], size, part).ShouldBe([2]);
        PortalContentRules.PartsNotReceived([(1, part), (2, part - 1), (3, 100)], size, part).ShouldBe([2]);
        PortalContentRules.PartsNotReceived([], size, part).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Keys_are_random_under_the_project_and_never_hold_the_name()
    {
        var first = PortalContentRules.NewStorageKey(12);
        var second = PortalContentRules.NewStorageKey(12);

        first.ShouldStartWith("portal/12/");
        first.ShouldNotBe(second);
        first.Length.ShouldBe("portal/12/".Length + 32);
    }

    [Fact]
    public void Names_lose_their_path_and_titles_their_extension()
    {
        PortalContentRules.SafeName(@"C:\fakepath\Living room.jpg").ShouldBe("Living room.jpg");
        PortalContentRules.SafeName("../../etc/plan.pdf").ShouldBe("plan.pdf");
        PortalContentRules.SafeName(new string('a', 300) + ".pdf").Length.ShouldBe(PortalContentRules.MaxNameLength);
        PortalContentRules.SafeName(new string('a', 300) + ".pdf").ShouldEndWith(".pdf");

        PortalContentRules.DefaultTitle("Living room v3.jpg").ShouldBe("Living room v3");
        PortalContentRules.DefaultTitle(new string('b', 250) + ".jpg").Length.ShouldBe(PortalContentRules.MaxTitleLength);
    }

    [Theory]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(995)]
    public void A_long_name_is_cut_to_fit_however_long_its_extension(int extensionLength)
    {
        var name = PortalContentRules.SafeName("plan." + new string('x', extensionLength));

        name.Length.ShouldBe(PortalContentRules.MaxNameLength);
        name.ShouldStartWith("p");
    }

    [Theory]
    [InlineData(0x202E)] // right-to-left override: it would show "photo" + RLO + "gpj.exe" as "photoexe.jpg"
    [InlineData(0x200B)] // zero width space
    [InlineData(0x200D)] // zero width joiner
    [InlineData(0x2060)] // word joiner
    [InlineData(0xFEFF)] // byte order mark
    [InlineData(0x00AD)] // soft hyphen
    [InlineData(0x0007)] // a control character
    [InlineData(0x0009)] // a tab
    public void Names_lose_control_and_invisible_formatting_characters(int code)
    {
        var hidden = (char)code;

        PortalContentRules.SafeName($"{hidden}Living{hidden} room{hidden}.jpg").ShouldBe("Living room.jpg");
    }

    [Fact]
    public void Names_keep_the_letters_and_punctuation_people_type()
        => PortalContentRules.SafeName("Façade – 3D (v2).glb").ShouldBe("Façade – 3D (v2).glb");

    [Fact]
    public void New_items_go_after_the_last_with_room_between()
    {
        PortalContentRules.NextSortOrder(null).ShouldBe(1024);
        PortalContentRules.NextSortOrder(3072).ShouldBe(4096);
    }

    [Fact]
    public void Only_client_projects_that_are_not_archived_can_change()
    {
        PortalContentRules.CheckEditable(BillingType.Client, 7, ProjectStatus.Active).ShouldBeNull();
        PortalContentRules.CheckEditable(BillingType.Client, 7, ProjectStatus.Completed).ShouldBeNull();
        PortalContentRules.CheckEditable(BillingType.Client, 7, ProjectStatus.Hold).ShouldBeNull();
        PortalContentRules.CheckEditable(BillingType.Client, 7, ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.PortalContentReadOnly);
        PortalContentRules.CheckEditable(BillingType.Internal, null, ProjectStatus.Active)!.Code.ShouldBe(ErrorCodes.PortalProjectNotEligible);
    }
}
