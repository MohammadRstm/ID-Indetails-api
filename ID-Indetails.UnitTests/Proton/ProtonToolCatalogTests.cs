using System.Reflection;
using System.Text.RegularExpressions;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Tools;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>Guarantees about the tool set as a whole: read-only, authorized like the API, and without the excluded data.</summary>
public class ProtonToolCatalogTests
{
    private static readonly Type[] ToolTypes = typeof(IProtonTool).Assembly.GetTypes()
        .Where(t => typeof(IProtonTool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .ToArray();

    private static readonly IReadOnlyList<IProtonTool> Tools = ToolTypes.Select(Create).ToList();

    [Fact]
    public void Every_tool_is_registered_once_with_a_unique_snake_case_name()
    {
        var registered = new ServiceCollection().AddProton()
            .Where(d => d.ServiceType == typeof(IProtonTool))
            .Select(d => d.ImplementationType!)
            .ToList();

        registered.ShouldBe(ToolTypes, ignoreOrder: true);
        Tools.Select(t => t.Name).ShouldBeUnique();
        Tools.ShouldAllBe(t => Regex.IsMatch(t.Name, "^[a-z][a-z_]{2,40}$"));
        Tools.ShouldAllBe(t => t.Description.Length > 20);
    }

    [Fact]
    public void Every_tool_inherits_the_permission_policies_of_a_GET_endpoint()
    {
        foreach (var tool in Tools)
        {
            tool.Endpoint.Policies.ShouldNotBeEmpty(tool.Name);
            tool.Endpoint.Policies.ShouldAllBe(p => p.StartsWith(AuthorizationPolicies.PermissionPrefix), tool.Name);
        }

        // The mirror reads the real attributes, including a class-level one…
        ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary)).Policies
            .ShouldBe([AuthorizationPolicies.PermissionPrefix + PermissionCodes.FinancialView]);
        // …all of an action's attributes, which must all pass…
        ProtonEndpoint.Of<ActivityController>(nameof(ActivityController.Today)).Policies.Count.ShouldBe(2);
        // …and keeps reads and writes apart: a read tool can't stand in for anything that writes, nor an action for a read.
        Should.Throw<InvalidOperationException>(() => ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Create)));
        Should.Throw<InvalidOperationException>(() => ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Get)));
    }

    /// <summary>Only action tools touch write endpoints, and only the approved low/medium-risk ones, never money or deletion.</summary>
    [Fact]
    public void Only_action_tools_stand_in_for_write_endpoints_and_none_moves_money_or_deletes()
    {
        foreach (var tool in Tools)
        {
            tool.Endpoint.Writes.ShouldBe(tool is IProtonActionTool, tool.Name);
            (tool is IProtonActionTool).ShouldBe(tool.Name.StartsWith("propose_"), tool.Name);
        }

        string[] money =
        [
            PermissionCodes.FinancialView, PermissionCodes.InvoiceCreate, PermissionCodes.InvoiceSend, PermissionCodes.InvoiceVoid,
            PermissionCodes.PaymentRecord, PermissionCodes.PaymentVoid, PermissionCodes.ExpenseManage, PermissionCodes.EmployeePayManage,
            PermissionCodes.ProjectPricingManage, PermissionCodes.BillingSettingsManage, PermissionCodes.ExchangeRateManage,
        ];
        var actions = Tools.OfType<IProtonActionTool>().ToList();
        actions.Select(a => a.Name).ShouldBe(
        [
            "propose_add_requirement", "propose_update_requirement", "propose_set_requirement_done",
            "propose_change_project_status", "propose_update_project", "propose_create_project",
        ], ignoreOrder: true);
        foreach (var action in actions)
        {
            action.Endpoint.Name.ShouldNotContain("Delete", Case.Insensitive, action.Name);
            action.Endpoint.Policies.ShouldAllBe(p => !money.Any(m => p.Contains(m)), action.Name);
        }
    }

    [Fact]
    public void Every_input_schema_is_an_object_the_model_can_fill()
    {
        foreach (var tool in Tools)
        {
            var schema = tool.InputSchema;
            schema["type"]!.ToString().ShouldContain("object", customMessage: tool.Name);
            schema["properties"].ShouldNotBeNull(tool.Name);
        }

        Tools.Single(t => t.Name == "find").InputSchema["required"]!.AsArray().Select(r => r!.ToString()).ShouldBe(["text"]);
    }

    /// <summary>Pay, rates, admin salaries (and the net profit that reveals them), bank details and secrets never reach the model.</summary>
    [Fact]
    public void No_tool_output_can_carry_the_excluded_fields()
    {
        string[] excluded =
        [
            "HourlyRate", "StandardHourlyRate", "LabourCost", "LabourByPerson", "CostPerHour", "AdminSalaries",
            "UnconvertedAdminSalaries", "NetProfit", "CurrentPay", "Pay", "PayHistory", "Salary", "BankAccount",
            "BankName", "Iban", "Swift", "PasswordHash", "Token", "ApiKey",
        ];
        var outputs = typeof(IProtonTool).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(IProtonTool).Namespace && !typeof(IProtonTool).IsAssignableFrom(t) && !t.Name.EndsWith("Input"));

        var properties = new HashSet<string>();
        var seen = new HashSet<Type>();
        foreach (var type in outputs)
        {
            Collect(type, properties, seen);
        }

        properties.Intersect(excluded).ShouldBeEmpty();
    }

    private static void Collect(Type type, HashSet<string> properties, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Collect(argument, properties, seen);
            }

            return;
        }

        if (!type.Namespace?.StartsWith("ID_Indetails.api") == true || type.IsEnum || !seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            properties.Add(property.Name);
            Collect(property.PropertyType, properties, seen);
        }
    }

    /// <summary>A tool with mocked services: enough to read its name, endpoint and schema.</summary>
    private static IProtonTool Create(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(p => ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))!).Object)
            .ToArray();
        return (IProtonTool)constructor.Invoke(arguments);
    }
}
