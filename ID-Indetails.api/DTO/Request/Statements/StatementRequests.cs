using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Statements;

/// <summary>What a new statement shows. The server works out every line and price.</summary>
public sealed record CreateStatementRequest
{
    /// <summary><c>detail</c>: one row per material type. <c>total</c>: a single line.</summary>
    public StatementMode Mode { get; init; } = StatementMode.Detail;

    /// <summary>Show materials from earlier statements again.</summary>
    public bool IncludeEarlier { get; init; }

    /// <summary>Detailed statements only: new materials left out of this statement.</summary>
    public IReadOnlyList<long> ExcludedMaterialIds { get; init; } = [];

    /// <summary>Detailed statements only: earlier types with nothing new, collapsed into one line.</summary>
    public IReadOnlyList<string> UnlistedTypeKeys { get; init; } = [];
}

public sealed record StatementListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 10;
}
