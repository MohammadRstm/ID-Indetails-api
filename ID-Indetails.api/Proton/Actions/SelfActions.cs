using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record UpdateMyProfileArgs(UpdateMyProfileRequest Request, UpdateMyProfileRequest Before);

public sealed record UpdateMyProfileInput : ProtonActionInput
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    [Description("The user's new phone number; an empty string removes it.")]
    public string? Phone { get; init; }
}

/// <summary>
/// Changes the user's own name or phone, through PUT /api/me/profile once confirmed. Only ever the person asking:
/// there is no id to give.
/// </summary>
public sealed class ProposeUpdateMyProfileTool(IAuthService auth, IEmployeeService employees)
    : ProtonActionTool<UpdateMyProfileInput, UpdateMyProfileArgs>
{
    public override string Name => "propose_update_my_profile";

    public override string Description =>
        "Propose changing the user's own first name, last name or phone. Email, position, team and pay are the company's " +
        "to change. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfSelf<ProfileController>(nameof(ProfileController.UpdateProfile));

    protected override async Task<(UpdateMyProfileArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateMyProfileInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var before = await CurrentAsync(context, cancellationToken);
        var after = new UpdateMyProfileRequest
        {
            FirstName = input.FirstName?.Trim() ?? before.FirstName,
            LastName = input.LastName?.Trim() ?? before.LastName,
            Phone = input.Phone is null ? before.Phone : string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
        };

        var changes = new List<ProtonChange>();
        if (after.FirstName != before.FirstName)
        {
            changes.Add(new ProtonChange("First name", before.FirstName, after.FirstName));
        }

        if (after.LastName != before.LastName)
        {
            changes.Add(new ProtonChange("Last name", before.LastName, after.LastName));
        }

        if (after.Phone != before.Phone)
        {
            changes.Add(new ProtonChange("Phone", before.Phone ?? "none", after.Phone ?? "none"));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new UpdateMyProfileArgs(after, before), new ProtonPreview("Change your profile", changes), null);
    }

    protected override object RequestOf(UpdateMyProfileArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(UpdateMyProfileArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (await CurrentAsync(context, cancellationToken) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Your profile changed since Proton proposed this. Ask again.");
        }

        var me = await employees.UpdateMyProfileAsync(args.Request, cancellationToken);
        return new ProtonActionDone("Updated your profile.", "person", me.Id, []);
    }

    private async Task<UpdateMyProfileRequest> CurrentAsync(ProtonToolContext context, CancellationToken cancellationToken)
    {
        var me = await auth.GetCurrentUserAsync(context.Access.UserId, cancellationToken);
        return new UpdateMyProfileRequest { FirstName = me.FirstName, LastName = me.LastName, Phone = me.Phone };
    }
}
