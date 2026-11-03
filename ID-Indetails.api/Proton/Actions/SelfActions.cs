using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.BoardLayouts;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.BoardLayouts.Abstraction;
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

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProfileController>(nameof(ProfileController.UpdateProfile));

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

/// <param name="Request">The order to save; null puts the board back in its default order.</param>
/// <param name="Before">The saved order when proposed (null while nothing was saved): if it changed since, the change is refused.</param>
public sealed record ArrangeBoardArgs(string Key, SaveBoardLayoutRequest? Request, IReadOnlyList<string>? Before);

public sealed record ArrangeBoardInput : ProtonActionInput
{
    [Description("Which dashboard: design_overview, execution_overview or company_overview.")]
    public required ProtonBoard Board { get; init; }

    [Description("The complete new order: the key of every card from get_my_board_layout, each once. Give this or reset.")]
    public IReadOnlyList<string>? Order { get; init; }

    [Description("Put the cards back in their default order.")]
    public bool Reset { get; init; }
}

/// <summary>
/// Rearranges the cards of one of the user's own dashboards, through PUT /api/me/board-layouts/{key} once confirmed, or
/// puts them back in the default order, through DELETE (the app deletes the layout when the order matches the default
/// again). Only the user's own view changes.
/// </summary>
public sealed class ProposeArrangeBoardTool(IBoardLayoutService layouts) : ProtonActionTool<ArrangeBoardInput, ArrangeBoardArgs>
{
    public override string Name => "propose_arrange_board";

    public override string Description =>
        "Propose a new order for the cards on one of the user's own dashboards (read get_my_board_layout first), or " +
        "putting them back in the default order. It changes only the user's own view. Nothing changes until they confirm the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    // Save and Delete: a person's own card order, signed in is all it needs.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<BoardLayoutsController>(nameof(BoardLayoutsController.Save));

    protected override async Task<(ArrangeBoardArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ArrangeBoardInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Reset == (input.Order is { Count: > 0 }))
        {
            throw new ProtonToolInputException("Give the new order of the cards, or reset to put them back in the default order.");
        }

        var defaults = ProtonBoards.DefaultCards(input.Board, context.Access);
        if (defaults.Count == 0)
        {
            throw new ProtonToolInputException("The user has no such dashboard.");
        }

        var key = ProtonBoards.KeyOf(input.Board);
        var saved = (await layouts.GetAsync(key, cancellationToken)).Order;
        var current = ProtonBoards.Resolve(saved, defaults);
        var wanted = input.Reset ? defaults : CardsOf(input.Order!, defaults);
        if (wanted.Select(c => c.Key).SequenceEqual(current.Select(c => c.Key)))
        {
            throw new ProtonToolInputException("Nothing would change: the cards are already in that order.");
        }

        // like the app: an order that matches the default is no saved layout
        var back = wanted.Select(c => c.Key).SequenceEqual(defaults.Select(c => c.Key));
        var request = back ? null : new SaveBoardLayoutRequest { Order = wanted.Select(c => c.Key).ToList() };
        var name = ProtonBoards.NameOf(input.Board);
        return (new ArrangeBoardArgs(key, request, saved),
            new ProtonPreview(
                back ? $"Put your {name} cards back in the default order" : $"Rearrange your {name} cards",
                [new ProtonChange("Card order", ProtonBoards.Describe(current), ProtonBoards.Describe(wanted))],
                "Only your own view changes; everyone keeps theirs."),
            null);
    }

    /// <summary>The cards named, which must be every card of the board once.</summary>
    private static List<BoardCard> CardsOf(IReadOnlyList<string> order, IReadOnlyList<BoardCard> defaults)
    {
        var keys = order.Select(k => k.Trim()).ToList();
        var unknown = keys.Where(k => defaults.All(c => c.Key != k)).ToList();
        if (unknown.Count > 0 || keys.Distinct().Count() != keys.Count || keys.Count != defaults.Count)
        {
            throw new ProtonToolInputException(
                $"The order must list every card of the board once, by key: {string.Join(", ", defaults.Select(c => c.Key))}" +
                (unknown.Count > 0 ? $" (not cards of this board: {string.Join(", ", unknown)})." : "."));
        }

        return keys.Select(k => defaults.Single(c => c.Key == k)).ToList();
    }

    protected override object? RequestOf(ArrangeBoardArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ArrangeBoardArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var saved = (await layouts.GetAsync(args.Key, cancellationToken)).Order;
        if ((saved is null) != (args.Before is null) || (saved is not null && !saved.SequenceEqual(args.Before!)))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Your dashboard's order changed since Proton proposed this. Ask again.");
        }

        if (args.Request is null)
        {
            await layouts.DeleteAsync(args.Key, cancellationToken);
            return new ProtonActionDone("Put your cards back in the default order.", "board_layout", 0, []);
        }

        await layouts.SaveAsync(args.Key, args.Request, cancellationToken);
        return new ProtonActionDone("Saved the new order of your cards.", "board_layout", 0, []);
    }
}
