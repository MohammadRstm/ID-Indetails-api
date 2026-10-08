namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>Turns queued pushes into messages for each person's live phones and sends them (<c>Jobs/PushDispatcher</c>).</summary>
public interface IPushDelivery
{
    /// <summary>
    /// Checks each push again at send time (the person still active, still allowed to read it, with a live session),
    /// writes its text and target, and sends it to every live phone of the person.
    /// </summary>
    Task DeliverAsync(IReadOnlyList<PushJob> jobs, CancellationToken cancellationToken = default);

    /// <summary>Asks the push service about earlier sends and deletes the devices it no longer accepts.</summary>
    Task CheckReceiptsAsync(CancellationToken cancellationToken = default);
}
