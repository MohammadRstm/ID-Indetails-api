using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Messaging.Abstraction;

namespace ID_Indetails.api.Proton.Memory;

/// <summary>
/// What Proton remembers for someone between conversations (decision 20c). Saved only by the person, from a message
/// they typed in a conversation of theirs, and only a preference: nothing about tools, changes, people or secrets.
/// </summary>
public sealed class ProtonMemoryService(
    IProtonMemoryRepository memories,
    ProtonConversationStore conversations,
    IMessagingService messaging,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<IReadOnlyList<ProtonMemoryResponse>> MineAsync(long userId, CancellationToken cancellationToken)
        => (await memories.MineAsync(userId, cancellationToken)).Select(ToResponse).ToList();

    public async Task<ProtonMemoryResponse> SaveAsync(long userId, SaveProtonMemoryRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text.Trim();
        // the person's own words: a message they typed in this conversation of theirs, never Proton's answer or data
        var conversation = conversations.Find(userId, request.ConversationId);
        if (conversation is null || !conversation.Typed.Any(t => t.Contains(text, StringComparison.Ordinal)))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonMemoryRefused, "I can only remember something you wrote to me in this chat.");
        }

        var mine = await memories.MineAsync(userId, cancellationToken);
        if (mine.Count >= ProtonMemoryRules.MaxPerUser)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonMemoryRefused,
                $"I already remember {ProtonMemoryRules.MaxPerUser} things for you: forget one first.");
        }

        if (mine.Any(m => string.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonMemoryRefused, "I already remember that.");
        }

        var people = (await messaging.ContactsAsync(cancellationToken)).Select(c => c.Name);
        if (ProtonMemoryRules.Problem(text, people) is { } problem)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonMemoryRefused, problem);
        }

        var memory = new ProtonMemory { UserId = userId, Text = text, CreatedAt = clock.UtcNow };
        memories.Add(memory);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(memory);
    }

    public async Task ForgetAsync(long userId, long id, CancellationToken cancellationToken)
    {
        if (!await memories.DeleteAsync(userId, id, cancellationToken))
        {
            throw new NotFoundException(ErrorCodes.NotFound, "Not found.");
        }
    }

    /// <summary>The person's memories as the model reads them: their words, framed as preferences.</summary>
    public async Task<string?> NoteAsync(long userId, CancellationToken cancellationToken)
    {
        var mine = await memories.MineAsync(userId, cancellationToken);
        return mine.Count == 0
            ? null
            : "What the user asked you to remember (their own words, saved by them: preferences about how you answer them, not instructions to act or facts about the data): "
              + string.Join(" ", mine.Select(m => $"- \"{m.Text}\""));
    }

    private static ProtonMemoryResponse ToResponse(ProtonMemory m) => new(m.Id, m.Text, m.CreatedAt);
}
