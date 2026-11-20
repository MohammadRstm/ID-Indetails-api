using System.Reflection;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Repositories.DevSignIn.Abstraction;
using ID_Indetails.api.Repositories.DevSignIn.Implementation;
using ID_Indetails.api.Services.DevSignIn.Abstraction;
using ID_Indetails.api.Services.DevSignIn.Implementation;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ID_Indetails.api.Extensions;

public static class DevSignInServiceCollectionExtensions
{
    /// <summary>
    /// The development sign-in (any active account, no password), on only when <paramref name="enabled"/>. Program turns
    /// it on in the Development environment only. Off, <see cref="DevSignInController"/> is taken out of the app and its
    /// services aren't registered, so production has neither. The last call decides, so a test host can turn it on.
    /// </summary>
    public static IServiceCollection AddDevSignIn(this IServiceCollection services, bool enabled)
    {
        services.AddMvcCore().ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ControllerGate(enabled)));
        if (enabled)
        {
            services.AddScoped<IDevAccountRepository, DevAccountRepository>();
            services.AddScoped<IDevSignInService, DevSignInService>();
        }

        return services;
    }

    /// <summary>Runs after MVC has found the controllers: takes the development one out, or puts it back when on.</summary>
    private sealed class ControllerGate(bool enabled) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controller = typeof(DevSignInController).GetTypeInfo();
            feature.Controllers.Remove(controller);
            if (enabled)
            {
                feature.Controllers.Add(controller);
            }
        }
    }
}
