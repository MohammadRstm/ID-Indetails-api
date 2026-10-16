namespace ID_Indetails.api.Proton.Usage;

/// <summary>
/// The budget held by questions still being answered, so questions asked at once can't together spend past the
/// month's budget: each holds an estimate before the model is called and lets it go once its real cost is recorded.
/// In-process, like the permission cache: one API instance.
/// </summary>
public sealed class ProtonBudgetHolds
{
    private readonly object _lock = new();
    private decimal _held;

    /// <summary>Taken while a question's hold is decided, so the spend read and the hold added belong together.</summary>
    internal SemaphoreSlim Deciding { get; } = new(1, 1);

    public decimal Held
    {
        get { lock (_lock) return _held; }
    }

    public ProtonBudgetHold Add(decimal amount)
    {
        lock (_lock) _held += amount;
        return new ProtonBudgetHold(this, amount);
    }

    internal void Release(decimal amount)
    {
        lock (_lock) _held -= amount;
    }
}

/// <summary>One question's hold on the budget. Disposing it lets the amount go; only the first dispose counts.</summary>
public sealed class ProtonBudgetHold(ProtonBudgetHolds holds, decimal amount) : IDisposable
{
    private int _released;

    public decimal Amount => amount;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            holds.Release(amount);
        }
    }
}
