using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Company;

/// <summary>
/// The company's account labels: one list for the whole company, which payments and expenses are filed under.
/// Names are unique whatever their case, archived ones included.
/// </summary>
public static class AccountLabelRules
{
    public const int MaxName = 40;

    /// <summary>A new label goes last.</summary>
    public static int NextPosition(IEnumerable<int> existing) => existing.DefaultIfEmpty(0).Max() + 1;

    /// <summary>A new order lists exactly the labels that can be picked now, each once.</summary>
    public static RuleDenial? CheckOrder(IEnumerable<long> current, IReadOnlyList<long> order)
    {
        var have = current.ToHashSet();
        return order.Count == have.Count && order.Distinct().Count() == order.Count && order.All(have.Contains)
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.AccountLabelOrderStale, "The labels changed since you looked: one was added or archived. Your new order wasn't saved.");
    }
}
