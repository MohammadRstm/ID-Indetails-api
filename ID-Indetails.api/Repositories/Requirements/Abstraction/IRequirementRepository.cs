namespace ID_Indetails.api.Repositories.Requirements.Abstraction;

public interface IRequirementRepository
{
    /// <summary>How many live (not deleted) requirements the project has, and how many are done.</summary>
    Task<(int Total, int Done)> CountAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Ticks every open requirement as done automatically (when the project is finalised). Runs immediately.</summary>
    Task AutoTickOpenAsync(long projectId, DateTime doneAt, long doneById, CancellationToken cancellationToken = default);

    /// <summary>Reopens exactly the requirements that were ticked automatically. Runs immediately.</summary>
    Task UntickAutoDoneAsync(long projectId, CancellationToken cancellationToken = default);
}
