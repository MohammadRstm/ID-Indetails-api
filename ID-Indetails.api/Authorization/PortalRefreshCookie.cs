using ID_Indetails.api.Configuration.Options;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Authorization;

/// <summary>
/// The client portal's refresh cookie: HttpOnly, Secure, SameSite=Strict and scoped to the portal's auth endpoints, so it
/// never reaches the staff ones (and the staff cookie never reaches these). Staff and client can be signed in together.
/// </summary>
public sealed class PortalRefreshCookie(IOptions<PortalOptions> options)
{
    private readonly PortalOptions _options = options.Value;

    public string? Read(HttpRequest request) => request.Cookies[_options.CookieName];

    public void Write(HttpResponse response, string value, DateTime expiresAtUtc)
        => response.Cookies.Append(_options.CookieName, value, Build(new DateTimeOffset(expiresAtUtc, TimeSpan.Zero)));

    public void Clear(HttpResponse response)
        => response.Cookies.Delete(_options.CookieName, Build(expires: null));

    private CookieOptions Build(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = _options.CookiePath,
        Expires = expires,
        IsEssential = true,
    };
}
