using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Proton.Safety;

/// <summary>
/// The read-only guarantee beyond the database. While a Proton tool runs (looking something up, or preparing a proposal),
/// these wrappers refuse every way a service could reach someone or leave a trace: sending an email, writing or deleting
/// a file or a client portal upload, staging a notification, or signalling a live client. Reads pass through. Outside a tool they change nothing.
/// </summary>
public static class ProtonSideEffectGuards
{
    /// <summary>Wraps the current registrations of the five services. Call after they are registered (and again after a test replaces one).</summary>
    public static IServiceCollection AddProtonSideEffectGuards(this IServiceCollection services)
        => services
            .Guard<IEmailSender>(inner => new GuardedEmailSender(inner))
            .Guard<IFileStorage>(inner => new GuardedFileStorage(inner))
            .Guard<IContentStore>(inner => new GuardedContentStore(inner))
            .Guard<INotificationSender>(inner => new GuardedNotificationSender(inner))
            .Guard<ILiveUpdates>(inner => new GuardedLiveUpdates(inner));

    /// <summary>Replaces the last registration of <typeparamref name="T"/> with a wrapper around it, keeping its lifetime.</summary>
    private static IServiceCollection Guard<T>(this IServiceCollection services, Func<T, T> wrap) where T : class
    {
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} must be registered before Proton guards it.");
        services.Remove(inner);
        services.Add(new ServiceDescriptor(typeof(T), provider => wrap((T)Create(provider, inner)), inner.Lifetime));
        return services;
    }

    private static object Create(IServiceProvider provider, ServiceDescriptor descriptor)
        => descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    private sealed class GuardedEmailSender(IEmailSender inner) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("sending an email");
            return inner.SendAsync(message, cancellationToken);
        }
    }

    private sealed class GuardedFileStorage(IFileStorage inner) : IFileStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("saving a file");
            return inner.SaveAsync(content, extension, cancellationToken);
        }

        public Stream OpenRead(string key) => inner.OpenRead(key);

        public void Delete(string key)
        {
            ProtonReadOnly.ThrowIfActive("deleting a file");
            inner.Delete(key);
        }
    }

    /// <summary>Starting, signing, completing, aborting and deleting are refused; reads and download links pass through.</summary>
    private sealed class GuardedContentStore(IContentStore inner) : IContentStore
    {
        public bool IsAvailable => inner.IsAvailable;

        public Task<string> StartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("starting a file upload");
            return inner.StartUploadAsync(key, contentType, cancellationToken);
        }

        public Task<string> GetPartUploadUrlAsync(string key, string uploadId, int partNumber, DateTime expiresAt, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("signing a file upload link");
            return inner.GetPartUploadUrlAsync(key, uploadId, partNumber, expiresAt, cancellationToken);
        }

        public Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default)
            => inner.ListPartsAsync(key, uploadId, cancellationToken);

        public Task CompleteUploadAsync(string key, string uploadId, IReadOnlyList<StoredPart> parts, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("completing a file upload");
            return inner.CompleteUploadAsync(key, uploadId, parts, cancellationToken);
        }

        public Task AbortUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("aborting a file upload");
            return inner.AbortUploadAsync(key, uploadId, cancellationToken);
        }

        public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
            => inner.GetSizeAsync(key, cancellationToken);

        public Task<byte[]> ReadStartAsync(string key, int count, CancellationToken cancellationToken = default)
            => inner.ReadStartAsync(key, count, cancellationToken);

        public Task<string> GetDownloadUrlAsync(
            string key, string fileName, string contentType, bool inline, DateTime expiresAt, CancellationToken cancellationToken = default)
            => inner.GetDownloadUrlAsync(key, fileName, contentType, inline, expiresAt, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("deleting a stored file");
            return inner.DeleteAsync(key, cancellationToken);
        }

        public IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken = default)
            => inner.ListAsync(prefix, cancellationToken);
    }

    private sealed class GuardedNotificationSender(INotificationSender inner) : INotificationSender
    {
        public Task SendAsync(NotificationDraft draft, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("sending a notification");
            return inner.SendAsync(draft, recipientIds, cancellationToken);
        }

        public Task SendDigestAsync(NotificationDraft draft, string digestKey, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("sending a notification");
            return inner.SendDigestAsync(draft, digestKey, recipientIds, cancellationToken);
        }

        public Task SendOnceAsync(NotificationDraft draft, string key, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("sending a notification");
            return inner.SendOnceAsync(draft, key, recipientIds, cancellationToken);
        }

        public Task<IReadOnlyList<long>> DomainMembersAsync(string domain, CancellationToken cancellationToken = default)
            => inner.DomainMembersAsync(domain, cancellationToken);

        public Task<IReadOnlyList<long>> HoldersAsync(string domain, string permission, CancellationToken cancellationToken = default)
            => inner.HoldersAsync(domain, permission, cancellationToken);
    }

    private sealed class GuardedLiveUpdates(ILiveUpdates inner) : ILiveUpdates
    {
        public void NotificationsChanged(IEnumerable<long> userIds)
        {
            ProtonReadOnly.ThrowIfActive("signalling a live update");
            inner.NotificationsChanged(userIds);
        }

        public void ChatChanged(long userId, LiveChat chat)
        {
            ProtonReadOnly.ThrowIfActive("signalling a live update");
            inner.ChatChanged(userId, chat);
        }

        public void PortalChanged(long projectId, IEnumerable<long> userIds, IEnumerable<long> accountIds)
        {
            ProtonReadOnly.ThrowIfActive("signalling a live update");
            inner.PortalChanged(projectId, userIds, accountIds);
        }

        public void PortalNotificationsChanged(IEnumerable<long> accountIds)
        {
            ProtonReadOnly.ThrowIfActive("signalling a live update");
            inner.PortalNotificationsChanged(accountIds);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ProtonReadOnly.ThrowIfActive("signalling a live update");
            return inner.FlushAsync(cancellationToken);
        }

        public void Discard() => inner.Discard();
    }
}
