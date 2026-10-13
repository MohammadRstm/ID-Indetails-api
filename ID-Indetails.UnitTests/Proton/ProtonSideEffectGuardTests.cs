using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonSideEffectGuardTests
{
    private static readonly EmailMessage Email = new("someone@example.com", null, "Hi", "<p>Hi</p>", "Hi");

    private static (ServiceProvider Provider, Mock<IEmailSender> Email, Mock<IFileStorage> Files, Mock<INotificationSender> Notifications, Mock<ILiveUpdates> Live, Mock<IContentStore> Content) Build()
    {
        var email = new Mock<IEmailSender>();
        var files = new Mock<IFileStorage>();
        var notifications = new Mock<INotificationSender>();
        var live = new Mock<ILiveUpdates>();
        var content = new Mock<IContentStore>();
        var provider = new ServiceCollection()
            .AddSingleton(email.Object)
            .AddSingleton(files.Object)
            .AddSingleton(content.Object)
            .AddScoped(_ => notifications.Object)
            .AddScoped(_ => live.Object)
            .AddProtonSideEffectGuards()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return (provider, email, files, notifications, live, content);
    }

    [Fact]
    public async Task While_a_tool_runs_nothing_reaches_anyone_and_reads_still_work()
    {
        var (provider, email, files, notifications, live, content) = Build();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        await ProtonReadOnly.RunAsync(async () =>
        {
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => services.GetRequiredService<IEmailSender>().SendAsync(Email));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => services.GetRequiredService<IFileStorage>().SaveAsync(Stream.Null, ".txt"));
            Should.Throw<ProtonWriteRefusedException>(() => services.GetRequiredService<IFileStorage>().Delete("k"));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => services.GetRequiredService<INotificationSender>()
                .SendAsync(new NotificationDraft("k", null, null, null, null, null, null), [1]));
            Should.Throw<ProtonWriteRefusedException>(() => services.GetRequiredService<ILiveUpdates>().ChatChanged(1, new LiveChat(null, 2)));

            var store = services.GetRequiredService<IContentStore>();
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => store.StartUploadAsync("portal/1/k", "application/pdf"));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => store.GetPartUploadUrlAsync("portal/1/k", "u", 1, DateTime.UtcNow));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => store.CompleteUploadAsync("portal/1/k", "u", []));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => store.AbortUploadAsync("portal/1/k", "u"));
            await Should.ThrowAsync<ProtonWriteRefusedException>(() => store.DeleteAsync("portal/1/k"));

            // reads go through
            services.GetRequiredService<IFileStorage>().OpenRead("k");
            await services.GetRequiredService<INotificationSender>().HoldersAsync("design", "x");
            await store.ListPartsAsync("portal/1/k", "u");
            await store.GetSizeAsync("portal/1/k");
            await store.ReadStartAsync("portal/1/k", 8);
            await store.GetDownloadUrlAsync("portal/1/k", "a.pdf", "application/pdf", true, DateTime.UtcNow);
            _ = store.IsAvailable;
            return true;
        });

        email.VerifyNoOtherCalls();
        files.Verify(f => f.OpenRead("k"), Times.Once);
        files.VerifyNoOtherCalls();
        notifications.Verify(n => n.HoldersAsync("design", "x", default), Times.Once);
        notifications.VerifyNoOtherCalls();
        live.VerifyNoOtherCalls();
        content.Verify(c => c.ListPartsAsync("portal/1/k", "u", default), Times.Once);
        content.Verify(c => c.GetSizeAsync("portal/1/k", default), Times.Once);
        content.Verify(c => c.ReadStartAsync("portal/1/k", 8, default), Times.Once);
        content.Verify(c => c.GetDownloadUrlAsync("portal/1/k", "a.pdf", "application/pdf", true, It.IsAny<DateTime>(), default), Times.Once);
        content.VerifyGet(c => c.IsAvailable, Times.Once);
        content.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Outside_a_tool_everything_passes_through_with_its_lifetime_kept()
    {
        var (provider, email, _, _, live, _) = Build();

        await provider.GetRequiredService<IEmailSender>().SendAsync(Email);
        email.Verify(e => e.SendAsync(Email, default), Times.Once);
        provider.GetRequiredService<IEmailSender>().ShouldBeSameAs(provider.GetRequiredService<IEmailSender>());

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ILiveUpdates>().NotificationsChanged([3]);
        live.Verify(l => l.NotificationsChanged(new long[] { 3 }), Times.Once);
        // scoped stays scoped: resolving from the root is refused
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<ILiveUpdates>());
    }

    [Fact]
    public void Guarding_a_service_that_isnt_registered_fails_loudly()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddProtonSideEffectGuards());
}
