namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// The proxies in front of the API in production (the client's nginx, and the TLS proxy before it). Their
/// X-Forwarded-For and X-Forwarded-Proto headers are trusted only from these networks, so the rate limits and logs
/// see the visitor's address and HTTPS. Empty (the default, and in development) means no proxy: the headers are ignored.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>CIDR ranges the proxies' addresses are in, e.g. the Docker network "172.16.0.0/12".</summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>How many proxies stand in front: the client's nginx and the TLS proxy.</summary>
    public int ForwardLimit { get; init; } = 2;
}
