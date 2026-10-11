using System.Security.Claims;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Prompts;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Voice;

/// <summary>
/// Starts and ends voice sessions. A session is only the person's ears and voice: every question in it goes to the
/// same Proton conversation, tools and checks as typed ones, so it grants nothing the person's account doesn't. The
/// provider key stays here; the browser gets the provider's connection answer and the session's id, nothing else.
/// </summary>
public sealed class ProtonVoiceService(
    IProtonVoiceProvider provider,
    ProtonVoiceSessions sessions,
    ProtonVoiceWatch watch,
    ProtonConversationStore conversations,
    ProtonUsageService usage,
    IPermissionService permissions,
    IClock clock,
    IOptions<ProtonOptions> options,
    IOptions<EmailOptions> email)
{
    public async Task<ProtonVoiceSessionResponse> StartAsync(
        ClaimsPrincipal caller, long userId, StartProtonVoiceRequest request, CancellationToken cancellationToken)
    {
        var voice = options.Value.Voice;
        if (!voice.IsOn)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonVoiceUnavailable, "Voice isn't switched on. You can still type your question.");
        }

        await usage.EnsureCanAskAsync(userId, cancellationToken);
        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        var conversation = conversations.Open(userId, request.ConversationId, ProtonToolbox.FingerprintOf(access));

        var now = clock.UtcNow;
        var session = new ProtonVoiceSession(userId, conversation.Id, now, now.AddMinutes(voice.MaxSessionMinutes));
        if (!sessions.TryReserve(session))
        {
            throw new ConflictException(ErrorCodes.ProtonVoiceBusy, "You already have a voice conversation open. End it first, or carry on there.");
        }

        ProtonVoiceStarted started;
        try
        {
            var name = caller.FindFirst(AuthClaimTypes.FirstName)?.Value;
            var instructions = ProtonPrompts.VoiceFor(email.Value.ProductName)
                + (string.IsNullOrWhiteSpace(name) ? "" : $"\n\nThe person you're talking with is called {name}.");
            started = await provider.StartAsync(
                new ProtonVoiceStart(userId, instructions, conversation.Exchanges, request.Sdp), cancellationToken);
        }
        catch
        {
            sessions.Release(session);
            throw;
        }

        session.Started(started.SessionId);
        watch.Watch(session);
        return new ProtonVoiceSessionResponse(started.SessionId, started.SdpAnswer, conversation.Id, session.EndsAt);
    }

    /// <summary>Ends the person's session: the server closes it with the provider and records what it used. Ending one that already ended is fine.</summary>
    public void End(long userId, string sessionId) => sessions.Find(userId, sessionId)?.Close();
}
