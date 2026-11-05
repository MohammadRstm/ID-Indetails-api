using System.ComponentModel;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Services.BoardLayouts.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>The dashboards whose cards a person can rearrange (<see cref="BoardKeys"/>), as the model names them.</summary>
public enum ProtonBoard
{
    DesignOverview,
    ExecutionOverview,
    CompanyOverview,
}

public sealed record BoardCard(string Key, string Label);

/// <summary>
/// The cards of each rearrangeable dashboard, in their default order. The API stores only the order of card keys and the
/// client owns the cards (it drops a key it no longer knows and puts a new card at its default spot), so this mirrors
/// the client's boards for Proton: a card that is missing here keeps its default place, and a key the client doesn't
/// know is ignored. The money cards are for people who see money, as on the page.
/// </summary>
public static class ProtonBoards
{
    public static string KeyOf(ProtonBoard board) => board switch
    {
        ProtonBoard.DesignOverview => BoardKeys.DesignOverview,
        ProtonBoard.ExecutionOverview => BoardKeys.ExecutionOverview,
        _ => BoardKeys.CompanyOverview,
    };

    public static string NameOf(ProtonBoard board) => board switch
    {
        ProtonBoard.DesignOverview => "Design overview",
        ProtonBoard.ExecutionOverview => "Execution overview",
        _ => "Company overview",
    };

    /// <summary>The cards the person's page shows, in the order it shows them without a saved layout; empty for a board they can't open.</summary>
    public static IReadOnlyList<BoardCard> DefaultCards(ProtonBoard board, UserAccess access)
    {
        var cards = new List<BoardCard>();
        switch (board)
        {
            case ProtonBoard.DesignOverview when access.CanSee(DomainCodes.Design):
                cards.AddRange([
                    new("figures", "Key figures"), new("hours", "Hours logged per week"), new("followups", "Needs attention & deadlines"),
                    new("time", "Where the time went"), new("status", "Projects by status")]);
                AddMoney(cards, access);
                break;
            case ProtonBoard.ExecutionOverview when access.CanSee(DomainCodes.Execution):
                cards.AddRange([new("figures", "Key figures"), new("status", "Projects by status")]);
                AddMoney(cards, access);
                break;
            case ProtonBoard.CompanyOverview when access.Has(PermissionCodes.CompanyView):
                cards.AddRange([new("keeps", "What the company keeps"), new("across", "Across both")]);
                cards.AddRange(access.VisibleBusinesses.Select(d => new BoardCard(d, char.ToUpperInvariant(d[0]) + d[1..])));
                break;
        }

        return cards;
    }

    /// <summary>The saved order reconciled with today's cards, as the client does: gone cards drop out, new ones join where they are by default.</summary>
    public static IReadOnlyList<BoardCard> Resolve(IReadOnlyList<string>? saved, IReadOnlyList<BoardCard> defaults)
    {
        if (saved is null)
        {
            return defaults;
        }

        var order = saved.Where(key => defaults.Any(c => c.Key == key)).Distinct().ToList();
        for (var i = 0; i < defaults.Count; i++)
        {
            if (!order.Contains(defaults[i].Key))
            {
                order.Insert(Math.Min(i, order.Count), defaults[i].Key);
            }
        }

        return order.Select(key => defaults.Single(c => c.Key == key)).ToList();
    }

    public static string Describe(IEnumerable<BoardCard> cards) => string.Join(" · ", cards.Select((c, i) => $"{i + 1}. {c.Label}"));

    private static void AddMoney(List<BoardCard> cards, UserAccess access)
    {
        if (access.Has(PermissionCodes.FinancialView))
        {
            cards.AddRange([
                new("revcost", "Revenue against cost"), new("profit", "Profit by project"), new("invoiced", "Invoiced against collected"),
                new("owed", "Money owed to us"), new("clients", "Top clients")]);
        }
    }
}

public sealed record BoardInput
{
    [Description("Which dashboard: design_overview, execution_overview or company_overview.")]
    public required ProtonBoard Board { get; init; }
}

/// <param name="Cards">In the order the person's dashboard shows them now.</param>
/// <param name="Rearranged">The person saved their own order; otherwise it is the default.</param>
public sealed record BoardLayoutRow(string Board, IReadOnlyList<BoardCard> Cards, bool Rearranged, string? SavedAt);

/// <summary>The person's own card order for a dashboard, as GET /api/me/board-layouts/{key} returns it, with the cards in it.</summary>
public sealed class GetMyBoardLayoutTool(IBoardLayoutService layouts) : ProtonTool<BoardInput>
{
    public override string Name => "get_my_board_layout";

    public override string Description =>
        "The order of the cards on one of the user's dashboards (a Design or Execution overview, or the company overview), " +
        "with each card's key and name, and whether they rearranged it. Read it before proposing a new order.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<BoardLayoutsController>(nameof(BoardLayoutsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(BoardInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var defaults = ProtonBoards.DefaultCards(input.Board, context.Access);
        if (defaults.Count == 0)
        {
            throw new ProtonToolInputException("The user has no such dashboard.");
        }

        var saved = await layouts.GetAsync(ProtonBoards.KeyOf(input.Board), cancellationToken);
        return ProtonToolOutput.Of(new BoardLayoutRow(
            ProtonBoards.NameOf(input.Board), ProtonBoards.Resolve(saved.Order, defaults), saved.Order is not null, saved.SavedAt?.ToString("yyyy-MM-dd")));
    }
}
