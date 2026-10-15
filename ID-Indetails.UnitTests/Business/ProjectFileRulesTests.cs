using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectFileRulesTests
{
    private const long Me = 7;
    private const long Someone = 8;

    private static readonly PortalContentProject ClientProject = new(1, DomainCodes.Design, "Villa", ProjectStatus.Active, BillingType.Client, 3);

    private static ProjectFilesAccess Caller(
        bool managesFiles = false, bool managesPortal = false, PortalContentProject? project = null)
        => new(project ?? ClientProject, Me, managesFiles, managesPortal);

    [Fact]
    public void A_file_is_changed_by_its_uploader_or_a_project_files_manager()
    {
        ProjectFileRules.CanEdit(Caller(), uploaderId: Me).ShouldBeTrue();
        ProjectFileRules.CanEdit(Caller(), uploaderId: Someone).ShouldBeFalse();
        ProjectFileRules.CanEdit(Caller(managesFiles: true), uploaderId: Someone).ShouldBeTrue();
        // managing the client portal isn't managing the files
        ProjectFileRules.CanEdit(Caller(managesPortal: true), uploaderId: Someone).ShouldBeFalse();
    }

    [Fact]
    public void Managers_start_a_files_comments_and_anyone_replies()
    {
        ProjectFileRules.CanComment(Caller(managesFiles: true), hasComments: false).ShouldBeTrue();
        ProjectFileRules.CanComment(Caller(), hasComments: false).ShouldBeFalse();
        ProjectFileRules.CanComment(Caller(), hasComments: true).ShouldBeTrue();
    }

    [Fact]
    public void Only_an_archived_projects_files_are_read_only()
    {
        ProjectFileRules.CheckEditable(ProjectStatus.Active).ShouldBeNull();
        ProjectFileRules.CheckEditable(ProjectStatus.Hold).ShouldBeNull();
        ProjectFileRules.CheckEditable(ProjectStatus.Completed).ShouldBeNull();
        ProjectFileRules.CheckEditable(ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.PortalContentReadOnly);
    }

    [Fact]
    public void Only_client_portal_managers_choose_for_the_client_on_a_client_project_that_isnt_archived()
    {
        ProjectFileRules.CheckClientPortal(Caller(managesPortal: true)).ShouldBeNull();

        var withoutPermission = ProjectFileRules.CheckClientPortal(Caller(managesFiles: true))!;
        withoutPermission.Kind.ShouldBe(DenialKind.Forbidden);
        withoutPermission.Code.ShouldBe(ErrorCodes.Forbidden);

        var internalProject = ClientProject with { BillingType = BillingType.Internal, ClientId = null };
        ProjectFileRules.CheckClientPortal(Caller(managesPortal: true, project: internalProject))!.Code.ShouldBe(ErrorCodes.PortalProjectNotEligible);

        var archived = ClientProject with { Status = ProjectStatus.Archived };
        ProjectFileRules.CheckClientPortal(Caller(managesPortal: true, project: archived))!.Code.ShouldBe(ErrorCodes.PortalContentReadOnly);
    }

    [Fact]
    public void A_comment_reaches_the_uploader_and_earlier_commenters_once_each_but_never_its_author()
    {
        ProjectFileRules.CommentRecipients(uploaderId: 1, earlierAuthors: [2, 3, 1], authorId: 3).ShouldBe([1L, 2L]);
        ProjectFileRules.CommentRecipients(uploaderId: 1, earlierAuthors: [], authorId: 1).ShouldBeEmpty();
        ProjectFileRules.CommentRecipients(uploaderId: 1, earlierAuthors: [], authorId: 4).ShouldBe([1L]);
    }

    [Fact]
    public void A_comment_is_kept_trimmed_with_one_kind_of_line_break()
        => ProjectFileRules.Clean("  Darker sofa?\r\nOr lighter walls?  ").ShouldBe("Darker sofa?\nOr lighter walls?");
}
