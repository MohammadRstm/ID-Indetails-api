using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Permissions.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record PermissionsQuery
{
    [Description("Someone (from find): what they hold, what was given or taken away by hand, and what their role gives.")]
    public long? UserId { get; init; }

    [Description("Words in a permission's name or code, to find the one meant.")]
    public string? Search { get; init; }
}

public sealed record PermissionRow(string Code, string Description, string? Business, IReadOnlyList<string> RoleDefaults);

/// <param name="HeldByHand">Given on top of their role.</param>
/// <param name="TakenAway">Their role gives them, but they don't hold them.</param>
public sealed record PersonPermissionsCard(
    string Role, string? Business, bool IsJoker, IReadOnlyList<string> Holds, IReadOnlyList<string> HeldByHand, IReadOnlyList<string> TakenAway);

public sealed record PermissionsResult(IReadOnlyList<PermissionRow> Permissions, PersonPermissionsCard? Person);

/// <summary>Every permission with what it allows and who holds it by default, and what one person holds: as the permission screens return them.</summary>
public sealed class GetPermissionsTool(IPermissionManagementService permissions) : ProtonTool<PermissionsQuery>
{
    public override string Name => "get_permissions";

    public override string Description =>
        "The permission codes, with what each allows and which roles hold it by default (search by words), and for one " +
        "person (user_id) what they hold now, what was given on top of their role and what was taken away. Gives the " +
        "codes propose_permissions needs.";

    // the catalog and a person's permissions share one policy
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<PermissionsController>(nameof(PermissionsController.GetCatalog));

    protected override async Task<ProtonToolOutput> RunAsync(PermissionsQuery input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var words = (input.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = (await permissions.GetCatalogAsync(cancellationToken))
            .Where(p => words.All(w => p.Code.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PermissionRow(p.Code, p.Description, p.Domain, p.DefaultFor))
            .ToList();

        PersonPermissionsCard? person = null;
        if (input.UserId is { } userId)
        {
            var held = await permissions.GetForUserAsync(userId, cancellationToken);
            person = new PersonPermissionsCard(
                held.Role, held.Domain, held.IsJoker, held.Permissions,
                held.Overrides.Where(o => o.Effect == PermissionEffect.Grant).Select(o => o.Code).ToList(),
                held.Overrides.Where(o => o.Effect == PermissionEffect.Revoke).Select(o => o.Code).ToList());
        }

        return ProtonToolOutput.Of(new PermissionsResult(rows, person));
    }
}

public sealed record HourExcusesQuery
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("The month, YYYY-MM. Default this month.")]
    public string? Month { get; init; }
}

public sealed record HourExcuseRow(long Id, DateOnly Month, ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid, string? Note);

/// <summary>The hours excused in someone's month, as the salary detail lists them (without what anything is worth).</summary>
public sealed class ListHourExcusesTool(ISalaryService salaries) : ProtonTool<HourExcusesQuery>
{
    public override string Name => "list_hour_excuses";

    public override string Description =>
        "The hours excused in someone's month (minutes or days, paid or not, with the note): the ids propose_hour_excuse " +
        "needs to remove one. Never what anything is worth.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<SalariesController>(nameof(SalariesController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(HourExcusesQuery input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new SalaryMonthQuery { Month = input.Month?.Trim() };
        await context.ValidateAsync(query, cancellationToken);
        var detail = await salaries.GetAsync(input.UserId, query, cancellationToken);
        return ProtonToolOutput.Of(detail.Excuses
            .Select(e => new HourExcuseRow(e.Id, e.Month, e.Kind, e.Minutes, e.Days, e.IsPaid, ProtonText.Cut(e.Note, 200)))
            .ToList());
    }
}

public sealed record DeviceRow(long Id, DevicePlatform Platform, string? AppVersion, DateTime AddedAt, DateTime LastSeenAt);

/// <summary>The user's own phones that receive pushes, as GET /api/me/devices returns them (never a push token).</summary>
public sealed class ListMyDevicesTool(IDeviceService devices) : ProtonTool<NoSettingsInput>
{
    public override string Name => "list_my_devices";

    public override string Description =>
        "The user's own phones that get pushes (platform, app version, when added): the ids propose_remove_device needs.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<DevicesController>(nameof(DevicesController.List));

    protected override async Task<ProtonToolOutput> RunAsync(NoSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
        => ProtonToolOutput.Of((await devices.ListAsync(cancellationToken))
            .Select(d => new DeviceRow(d.Id, d.Platform, d.AppVersion, d.CreatedAt, d.UpdatedAt))
            .ToList());
}

/// <param name="QuietFrom">HH:mm in company time; null with <paramref name="QuietTo"/> when there are no quiet hours.</param>
/// <param name="MutedCategories">No pushes at all for these.</param>
public sealed record NotificationSettingsCard(string? QuietFrom, string? QuietTo, IReadOnlyList<NotificationCategory> MutedCategories, bool IsDefault);

/// <summary>The user's own quiet hours and muted push categories, as GET /api/me/notification-settings returns them.</summary>
public sealed class GetMyNotificationSettingsTool(INotificationSettingsService settings) : ProtonTool<NoSettingsInput>
{
    public override string Name => "get_my_notification_settings";

    public override string Description => "The user's own push settings: their quiet hours (company time) and the categories they muted.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<NotificationSettingsController>(nameof(NotificationSettingsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(NoSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var s = await settings.GetAsync(cancellationToken);
        return ProtonToolOutput.Of(new NotificationSettingsCard(Time(s.QuietFromMinute), Time(s.QuietToMinute), s.MutedCategories, s.IsDefault));
    }

    public static string? Time(int? minute) => minute is { } m ? $"{m / 60:00}:{m % 60:00}" : null;
}
