namespace ID_Indetails.api.Services.Auth.Abstraction;

public interface IPermissionService
{
    /// <summary>Effective permission codes for a role in a domain (see <c>Business/Auth/EffectivePermissions</c>).</summary>
    Task<IReadOnlyList<string>> GetEffectiveAsync(string roleCode, string? domainCode, CancellationToken cancellationToken = default);
}
