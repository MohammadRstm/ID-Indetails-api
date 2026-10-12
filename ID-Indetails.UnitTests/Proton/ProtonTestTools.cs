using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Tools;
using Microsoft.Extensions.Options;
using Moq;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>Every Proton tool of the API, built with mocked services: enough to read names, endpoints and schemas.</summary>
internal static class ProtonTestTools
{
    public static readonly Type[] Types = typeof(IProtonTool).Assembly.GetTypes()
        .Where(t => typeof(IProtonTool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .ToArray();

    public static readonly IReadOnlyList<IProtonTool> All = Types.Select(t => (IProtonTool)Build(t)).ToList();

    /// <summary>Mocks for interfaces; Proton's own sealed services (the analysis service) built from mocks in turn.</summary>
    private static object Build(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => Argument(p.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object Argument(Type type)
        => type == typeof(IOptions<ProtonOptions>) ? Options.Create(new ProtonOptions())
            : type.IsSealed ? Build(type)
            : ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;
}
