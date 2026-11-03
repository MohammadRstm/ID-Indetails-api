using Microsoft.AspNetCore.Hosting;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Every kind of change switched on, as a company that lets Proton do everything would: for the actions' own tests.</summary>
public sealed class ProtonAllActionsApiFactory : ProtonApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        foreach (var family in new[] { "Lifecycle", "Deletes", "Access", "CompanyRules", "PayRules", "Money", "Messaging", "Website" })
        {
            builder.UseSetting($"Proton:Actions:{family}", "true");
        }

        builder.UseSetting("Proton:Actions:MaxConfirmedActionsPerHour", "500");
    }
}
