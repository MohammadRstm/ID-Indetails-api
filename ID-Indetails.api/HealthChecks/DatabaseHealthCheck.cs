using ID_Indetails.api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ID_Indetails.api.HealthChecks;

/// <summary>Healthy when the database answers. The probe says only Healthy or Unhealthy, never why.</summary>
public sealed class DatabaseHealthCheck(IdIndetailsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
}
