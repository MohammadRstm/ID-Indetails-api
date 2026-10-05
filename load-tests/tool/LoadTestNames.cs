namespace ID_Indetails.LoadTests.Tool;

public static class LoadTestNames
{
    /// <summary>The only database the tool ever creates, changes or drops. Production's is <c>id_indetails</c>.</summary>
    public const string Database = "idi_loadtest";

    /// <summary>Holds the marker saying which load-test environment a database server belongs to.</summary>
    public const string IdentityDatabase = "idi_loadtest_identity";

    public const string Environment = "loadtest";
}
