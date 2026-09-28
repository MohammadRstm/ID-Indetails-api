using ID_Indetails.api.Configuration.Options;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Authorization;

/// <summary>
/// Reads and writes the refresh cookie: HttpOnly (no JavaScript access), Secure (HTTPS only),
/// SameSite (Strict by default) and scoped to the auth endpoints only.
/// </summary>
public sealed class RefreshTokenCookie(IOptions<RefreshTokenOptions> options)
{
    private readonly RefreshTokenOptions _options = options.Value;

    public string? Read(HttpRequest request) => request.Cookies[_options.CookieName];

    public void Write(HttpResponse response, string value, DateTime expiresAtUtc)
        => response.Cookies.Append(_options.CookieName, value, Build(new DateTimeOffset(expiresAtUtc, TimeSpan.Zero)));

    public void Clear(HttpResponse response)
        => response.Cookies.Delete(_options.CookieName, Build(expires: null));

    private CookieOptions Build(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = Enum.Parse<SameSiteMode>(_options.SameSite, ignoreCase: true),
        Path = _options.CookiePath,
        Expires = expires,
        IsEssential = true,
    };
}
