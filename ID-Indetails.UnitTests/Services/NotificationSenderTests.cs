using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Notifications.Implementation;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class NotificationSenderTests
{
    private const long Actor = 1;
    private const long DesignEmployee = 2;
    private const long ExecutionEmployee = 3;
    private const long Deactivated = 4;
    private const long SuperAdmin = 5;

    private static readonly DateTime Now = new(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly List<Notification> _staged = [];
    private readonly Mock<INotificationRepository> _repository = new();
    private readonly Mock<ILiveUpdates> _live = new();
    private readonly Mock<IPushQueue> _pushes = new();

    private NotificationSender CreateSender()
    {
        var access = new Dictionary<long, UserAccess>
        {
            [Actor] = new(Actor, RoleCodes.Admin, DomainCodes.Design, [PermissionCodes.MaterialManage], false, [DomainCodes.Design]),
            [DesignEmployee] = new(DesignEmployee, RoleCodes.Employee, DomainCodes.Design, [PermissionCodes.ProjectView], false, [DomainCodes.Design]),
            [ExecutionEmployee] = new(ExecutionEmployee, RoleCodes.Employee, DomainCodes.Execution, [PermissionCodes.MaterialManage], false, [DomainCodes.Execution]),
            [Deactivated] = UserAccess.None(Deactivated, RoleCodes.Employee, DomainCodes.Design),
            [SuperAdmin] = new(SuperAdmin, RoleCodes.SuperAdmin, null, [PermissionCodes.MaterialManage], false, [DomainCodes.Design, DomainCodes.Execution]),
        };
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.GetAccessAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => access[id]);
        _repository.Setup(r => r.Add(It.IsAny<Notification>())).Callback<Notification>(_staged.Add);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        return new NotificationSender(_repository.Object, new Mock<IUserRepository>().Object, permissions.Object, _live.Object, _pushes.Object, clock.Object);
    }

    [Fact]
    public async Task Sends_once_to_each_active_person_who_sees_the_domain_but_never_the_actor()
    {
        var sender = CreateSender();

        await sender.SendAsync(
            new NotificationDraft(NotificationKinds.RequirementAssigned, Actor, DomainCodes.Design, 10, "requirement", 20, new { text = "Stairs" }),
            [Actor, DesignEmployee, DesignEmployee, ExecutionEmployee, Deactivated, SuperAdmin]);

        _staged.Select(n => n.RecipientId).ShouldBe([DesignEmployee, SuperAdmin]);
        _live.Verify(l => l.NotificationsChanged(It.Is<IEnumerable<long>>(ids => ids.SequenceEqual(new[] { DesignEmployee, SuperAdmin }))));
        _pushes.Verify(p => p.Notifications(It.Is<IEnumerable<Notification>>(rows => rows.SequenceEqual(_staged)), false));
        _staged.ShouldAllBe(n => n.Kind == NotificationKinds.RequirementAssigned
            && n.ActorId == Actor && n.ProjectId == 10 && n.SubjectType == "requirement" && n.SubjectId == 20
            && n.Payload == """{"text":"Stairs"}""" && n.CreatedAt == Now);
    }

    [Fact]
    public async Task A_digest_repeat_bumps_the_existing_notification_and_marks_it_unread()
    {
        var sender = CreateSender();
        var existing = new Notification
        {
            RecipientId = DesignEmployee, Kind = NotificationKinds.RequirementsAdded, DigestKey = "key", Count = 2,
            ReadAt = Now.AddHours(-1), CreatedAt = Now.AddHours(-2),
        };
        _repository.Setup(r => r.GetDigestForUpdateAsync(DesignEmployee, "key", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await sender.SendDigestAsync(
            new NotificationDraft(NotificationKinds.RequirementsAdded, Actor, DomainCodes.Design, 10, "requirement", 21, new { latestText = "Roof" }),
            "key",
            [DesignEmployee, SuperAdmin]);

        existing.ShouldSatisfyAllConditions(
            n => n.Count.ShouldBe(3),
            n => n.SubjectId.ShouldBe(21),
            n => n.Payload.ShouldBe("""{"latestText":"Roof"}"""),
            n => n.CreatedAt.ShouldBe(Now),
            n => n.ReadAt.ShouldBeNull());
        _staged.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            n => n.RecipientId.ShouldBe(SuperAdmin),
            n => n.DigestKey.ShouldBe("key"),
            n => n.Count.ShouldBe(1));
        // both the bumped digest and the new one push, as digests
        _pushes.Verify(p => p.Notifications(It.Is<IEnumerable<Notification>>(rows => rows.SequenceEqual(new[] { existing, _staged[0] })), true));
    }

    [Fact]
    public async Task A_keyed_notification_skips_whoever_already_has_it()
    {
        var sender = CreateSender();
        _repository.Setup(r => r.RecipientsWithKeyAsync("weekly", It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<long> { DesignEmployee });

        await sender.SendOnceAsync(
            new NotificationDraft(NotificationKinds.WeeklySummary, null, DomainCodes.Design, null, null, null, new { minutes = 0 }),
            "weekly",
            [DesignEmployee, SuperAdmin]);

        _staged.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            n => n.RecipientId.ShouldBe(SuperAdmin),
            n => n.DigestKey.ShouldBe("weekly"),
            n => n.ActorId.ShouldBeNull());
    }

    [Fact]
    public async Task Company_level_notifications_reach_any_active_person()
    {
        var sender = CreateSender();

        await sender.SendAsync(
            new NotificationDraft(NotificationKinds.PayChanged, Actor, null, null, "user", ExecutionEmployee, new { }),
            [ExecutionEmployee, Deactivated]);

        _staged.ShouldHaveSingleItem().RecipientId.ShouldBe(ExecutionEmployee);
    }

    [Fact]
    public async Task Holders_are_the_people_who_hold_the_permission_and_see_the_domain()
    {
        var sender = CreateSender();
        _repository.Setup(r => r.PeopleWhoMayHoldAsync(DomainCodes.Execution, PermissionCodes.MaterialManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Actor, DesignEmployee, ExecutionEmployee, Deactivated, SuperAdmin]);

        (await sender.HoldersAsync(DomainCodes.Execution, PermissionCodes.MaterialManage)).ShouldBe([ExecutionEmployee, SuperAdmin]);
    }
}
