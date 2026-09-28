namespace ID_Indetails.api.Enums;

/// <summary>How a client statement lists materials.</summary>
public enum StatementMode
{
    /// <summary>One row per material type.</summary>
    Detail,

    /// <summary>A single "materials supplied since …" line.</summary>
    Total,
}
