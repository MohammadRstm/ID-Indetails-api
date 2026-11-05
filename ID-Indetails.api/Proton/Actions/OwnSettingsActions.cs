using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="PhotoFileId">The photo when proposed: if it changed since, the removal is refused.</param>
public sealed record RemoveMyPhotoArgs(long PhotoFileId);

public sealed record RemoveMyPhotoInput : ProtonActionInput;

/// <summary>Removes the user's own profile photo, through DELETE /api/me/photo once confirmed. It can't be undone: they upload another in the app.</summary>
public sealed class ProposeRemoveMyPhotoTool(IAuthService auth, IEmployeeService employees) : ProtonActionTool<RemoveMyPhotoInput, RemoveMyPhotoArgs>
{
    public override string Name => "propose_remove_my_photo";

    public override string Description =>
        "Propose removing the user's own profile photo (setting a new one needs a picture from their device: open their " +
        "profile for that). It is deleted for good. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProfileController>(nameof(ProfileController.RemovePhoto));

    protected override async Task<(RemoveMyPhotoArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RemoveMyPhotoInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var me = await auth.GetCurrentUserAsync(context.Access.UserId, cancellationToken);
        var photo = me.PhotoFileId ?? throw new ProtonToolInputException("The user has no profile photo.");
        return (new RemoveMyPhotoArgs(photo),
            new ProtonPreview("Remove your profile photo", [new ProtonChange("Profile photo", "set", "removed")],
                "The photo is deleted; you can upload another from your profile.", Confirm: ProtonConfirm.TwoStep, Irreversible: true),
            null);
    }

    protected override object? RequestOf(RemoveMyPhotoArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(RemoveMyPhotoArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await auth.GetCurrentUserAsync(context.Access.UserId, cancellationToken)).PhotoFileId != args.PhotoFileId)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Your profile photo changed since Proton proposed this. Ask again.");
        }

        var me = await employees.RemoveMyPhotoAsync(cancellationToken);
        return new ProtonActionDone("Removed your profile photo.", "person", me.Id, []);
    }
}

public sealed record MyProtonSuggestionsArgs(MyProtonSuggestionsRequest Request, bool Before);

public sealed record MyProtonSuggestionsInput : ProtonActionInput
{
    [Description("True to have Proton suggest replies to the user in the discussions with clients, false to stop it.")]
    public required bool Enabled { get; init; }
}

/// <summary>Turns Proton's reply suggestions on or off for the user alone, through PUT /api/me/proton-suggestions once confirmed.</summary>
public sealed class ProposeMyProtonSuggestionsTool(IAuthService auth, IEmployeeService employees)
    : ProtonActionTool<MyProtonSuggestionsInput, MyProtonSuggestionsArgs>
{
    public override string Name => "propose_my_proton_suggestions";

    public override string Description =>
        "Propose turning on or off the reply suggestions Proton gives the user in the discussions with clients. It is the " +
        "user's own choice and affects nobody else (the company switch is propose_company_proton). Nothing changes until " +
        "the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProfileController>(nameof(ProfileController.SetProtonSuggestions));

    protected override async Task<(MyProtonSuggestionsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        MyProtonSuggestionsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var before = (await auth.GetCurrentUserAsync(context.Access.UserId, cancellationToken)).ProtonReplySuggestions;
        if (before == input.Enabled)
        {
            throw new ProtonToolInputException($"Reply suggestions are already {SettingsText.OnOff(before).ToLowerInvariant()} for the user.");
        }

        return (new MyProtonSuggestionsArgs(new MyProtonSuggestionsRequest { Enabled = input.Enabled }, before),
            new ProtonPreview("Change your reply suggestions", [new ProtonChange("Proton's reply suggestions for you", SettingsText.OnOff(before), SettingsText.OnOff(input.Enabled))],
                "They appear in the discussions with clients, and only while the company has them on."),
            null);
    }

    protected override object RequestOf(MyProtonSuggestionsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(MyProtonSuggestionsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await auth.GetCurrentUserAsync(context.Access.UserId, cancellationToken)).ProtonReplySuggestions != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Your setting changed since Proton proposed this. Ask again.");
        }

        var me = await employees.SetMyProtonSuggestionsAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Reply suggestions are {SettingsText.OnOff(args.Request.Enabled).ToLowerInvariant()} for you.", "person", me.Id, []);
    }
}

/// <param name="Before">Their quiet hours and muted categories as proposed (<see cref="NotificationText.Signature"/>).</param>
public sealed record NotificationSettingsArgs(SaveNotificationSettingsRequest Request, string Before);

public sealed record NotificationSettingsInput : ProtonActionInput
{
    [Description("Quiet hours start, HH:mm in company time. Give it with quiet_to.")]
    public string? QuietFrom { get; init; }

    [Description("Quiet hours end, HH:mm in company time. Give it with quiet_from.")]
    public string? QuietTo { get; init; }

    [Description("Turn the quiet hours off.")]
    public bool NoQuietHours { get; init; }

    [Description("Push categories to mute: no pushes at all for them (the in-app list is unchanged).")]
    public IReadOnlyList<NotificationCategory>? Mute { get; init; }

    [Description("Push categories to let through again.")]
    public IReadOnlyList<NotificationCategory>? Unmute { get; init; }
}

/// <summary>
/// Changes the user's own quiet hours and muted push categories, through PUT /api/me/notification-settings once
/// confirmed. Only the parts named change; the rest of their settings stay as they are.
/// </summary>
public sealed class ProposeNotificationSettingsTool(INotificationSettingsService settings)
    : ProtonActionTool<NotificationSettingsInput, NotificationSettingsArgs>
{
    public override string Name => "propose_notification_settings";

    public override string Description =>
        "Propose changing the user's own push settings: their quiet hours (pushes arrive silently), turning them off, or " +
        "muting and unmuting categories (use get_my_notification_settings for the present ones). Nothing changes until " +
        "the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<NotificationSettingsController>(nameof(NotificationSettingsController.Save));

    protected override async Task<(NotificationSettingsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        NotificationSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var now = await settings.GetAsync(cancellationToken);
        var (from, to) = (now.QuietFromMinute, now.QuietToMinute);
        if (input.NoQuietHours)
        {
            if (input.QuietFrom is not null || input.QuietTo is not null)
            {
                throw new ProtonToolInputException("Either quiet hours or no_quiet_hours, not both.");
            }

            (from, to) = (null, null);
        }
        else if (input.QuietFrom is not null || input.QuietTo is not null)
        {
            if (input.QuietFrom is null || input.QuietTo is null)
            {
                throw new ProtonToolInputException("Quiet hours need both ends: quiet_from and quiet_to.");
            }

            (from, to) = (PayRulesText.Minute(input.QuietFrom), PayRulesText.Minute(input.QuietTo));
        }

        var mute = (input.Mute ?? []).Distinct().ToList();
        var unmute = (input.Unmute ?? []).Distinct().ToList();
        if (mute.Intersect(unmute).Any())
        {
            throw new ProtonToolInputException("A category can't be both muted and unmuted.");
        }

        var muted = now.MutedCategories.Union(mute).Except(unmute).Order().ToList();
        var request = new SaveNotificationSettingsRequest { QuietFromMinute = from, QuietToMinute = to, MutedCategories = muted };

        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Quiet hours", NotificationText.Quiet(now.QuietFromMinute, now.QuietToMinute), NotificationText.Quiet(from, to));
        SettingsText.Compare(changes, "Muted categories", NotificationText.Muted(now.MutedCategories), NotificationText.Muted(muted));
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new NotificationSettingsArgs(request, NotificationText.Signature(now.QuietFromMinute, now.QuietToMinute, now.MutedCategories)),
            new ProtonPreview("Change your push settings", changes,
                "Quiet hours deliver pushes silently; a muted category sends no pushes at all. The in-app list is unchanged."),
            null);
    }

    protected override object RequestOf(NotificationSettingsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(NotificationSettingsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var now = await settings.GetAsync(cancellationToken);
        if (NotificationText.Signature(now.QuietFromMinute, now.QuietToMinute, now.MutedCategories) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Your push settings changed since Proton proposed this. Ask again.");
        }

        await settings.SaveAsync(args.Request, cancellationToken);
        return new ProtonActionDone("Saved your push settings.", "notification_settings", context.Access.UserId, []);
    }
}

public sealed record RemoveDeviceArgs(long DeviceId);

public sealed record RemoveDeviceInput : ProtonActionInput
{
    [Description("From list_my_devices.")]
    public required long DeviceId { get; init; }
}

/// <summary>
/// Takes one of the user's own phones off their account, through DELETE /api/me/devices/{id} once confirmed: its
/// session ends and it gets no more pushes. Only the user's own; the phone signs in again from the app.
/// </summary>
public sealed class ProposeRemoveDeviceTool(IDeviceService devices) : ProtonActionTool<RemoveDeviceInput, RemoveDeviceArgs>
{
    public override string Name => "propose_remove_device";

    public override string Description =>
        "Propose signing one of the user's own phones out of their account (list_my_devices has the ids): its session " +
        "ends and it gets no more pushes. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<DevicesController>(nameof(DevicesController.Delete));

    protected override async Task<(RemoveDeviceArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RemoveDeviceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var device = (await devices.ListAsync(cancellationToken)).FirstOrDefault(d => d.Id == input.DeviceId)
            ?? throw new NotFoundException("Device not found.");
        return (new RemoveDeviceArgs(device.Id),
            new ProtonPreview("Sign a phone out of your account",
                [new ProtonChange("Phone", $"{device.Platform}{(device.AppVersion is null ? "" : $", app {device.AppVersion}")}, added {ProjectText.Date(DateOnly.FromDateTime(device.CreatedAt))}", "signed out")],
                "Its session ends and it gets no more pushes. It comes back by signing in again on the phone.",
                Confirm: ProtonConfirm.TwoStep),
            null);
    }

    protected override object? RequestOf(RemoveDeviceArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(RemoveDeviceArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await devices.DeleteAsync(args.DeviceId, cancellationToken);
        return new ProtonActionDone("Signed that phone out.", "device", args.DeviceId, []);
    }
}

internal static class NotificationText
{
    public static string Quiet(int? from, int? to) => from is null || to is null ? "none" : $"{GetMyNotificationSettingsTool.Time(from)}–{GetMyNotificationSettingsTool.Time(to)}";

    public static string Muted(IEnumerable<NotificationCategory> muted) => muted.Any() ? string.Join(", ", muted.Order()) : "none";

    public static string Signature(int? from, int? to, IEnumerable<NotificationCategory> muted) => $"{from}|{to}|{string.Join(',', muted.Order())}";
}
