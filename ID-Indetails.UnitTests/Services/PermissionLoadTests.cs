using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Implementation;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

/// <summary>A page opening sends many requests at once: a person's access is loaded once for all of them.</summary>
public class PermissionLoadTests
{
    private const long UserId = 7;

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly PermissionLoads _loads = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private TaskCompletionSource<UserRoleSnapshot?> _snapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PermissionLoadTests()
    {
        _users.Setup(u => u.GetRoleSnapshotAsync(UserId, It.IsAny<CancellationToken>())).Returns(() => _snapshot.Task);
        _permissions.Setup(p => p.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _permissions.Setup(p => p.GetGrantsByRoleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<EffectivePermissions.Grant>>());
        _permissions.Setup(p => p.GetOverridesAsync(UserId, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    // each request has its own service, as each has its own scope
    private PermissionService Service() => new(_permissions.Object, _users.Object, _cache, _loads);

    private static UserRoleSnapshot Admin => new(UserId, "admin", "design", UserStatus.Active, null, null);

    [Fact]
    public async Task Requests_arriving_together_share_one_load()
    {
        var requests = Enumerable.Range(0, 10).Select(_ => Service().GetAccessAsync(UserId)).ToList();
        _snapshot.SetResult(Admin);

        (await Task.WhenAll(requests)).ShouldAllBe(a => a.UserId == UserId);
        _users.Verify(u => u.GetRoleSnapshotAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        // and it is kept for the next ones
        await Service().GetAccessAsync(UserId);
        _users.Verify(u => u.GetRoleSnapshotAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _loads.Running.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_change_during_a_load_isnt_lost_to_what_the_load_read()
    {
        var before = Service().GetAccessAsync(UserId);
        // the role changes while the access is being read
        Service().Forget(UserId);
        _snapshot.SetResult(Admin);
        await before;

        // what was read before the change isn't kept: the next request reads again
        _snapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _snapshot.SetResult(Admin with { RoleCode = "employee" });
        (await Service().GetAccessAsync(UserId)).Role.ShouldBe("employee");
        _users.Verify(u => u.GetRoleSnapshotAsync(UserId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_waiting_request_can_give_up_without_stopping_the_load()
    {
        var loader = Service().GetAccessAsync(UserId);
        using var leaving = new CancellationTokenSource();
        var waiter = Service().GetAccessAsync(UserId, leaving.Token);
        await leaving.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(waiter);
        _snapshot.SetResult(Admin);
        (await loader).UserId.ShouldBe(UserId);
    }
}
