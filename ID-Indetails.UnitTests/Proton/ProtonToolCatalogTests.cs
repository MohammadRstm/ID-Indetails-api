using System.Reflection;
using System.Text.RegularExpressions;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Tools;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>Guarantees about the tool set as a whole: read-only, authorized like the API, and without the excluded data.</summary>
public class ProtonToolCatalogTests
{
    private static readonly Type[] ToolTypes = ProtonTestTools.Types;

    private static readonly IReadOnlyList<IProtonTool> Tools = ProtonTestTools.All;

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
    public void The_core_set_kept_out_of_tool_search_is_made_of_real_reads_and_moves()
    {
        ProtonToolSearch.Core.ShouldBeSubsetOf(Tools.Select(t => t.Name));
        Tools.Where(t => ProtonToolSearch.Core.Contains(t.Name)).ShouldAllBe(t => !(t is IProtonActionTool));
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
    public void Only_action_tools_stand_in_for_write_endpoints_and_money_and_deletes_only_in_their_families()
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
        // the actions that exist are exactly the ones the coverage registry lists as built (see ProtonCoverageTests)
        var actions = Tools.OfType<IProtonActionTool>().ToList();
        actions.Select(a => a.Name).ShouldBe(
            ProtonCoverage.Entries.Where(e => e.Kind == ProtonCoverageKind.Action).Select(e => e.Tool!).Distinct(), ignoreOrder: true);
        // a delete or a money write only ever in its own family, which is off until switched on (decisions 4 and 5); the
        // person's own work entry is the exception: it is restored from My work within the edit window
        foreach (var action in actions)
        {
            var family = ProtonCoverage.FamilyOf(action.Name);
            // deleting a cost or a material is a money change: off with the Money family
            if (action.Endpoint.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) && action.Endpoint.Name != "WorkEntries.Delete")
            {
                family.ShouldBeOneOf(ProtonFamily.Deletes, ProtonFamily.Money);
            }

            if (action.Endpoint.Policies.Any(p => money.Any(m => p.Contains(m))))
            {
                family.ShouldBe(ProtonFamily.Money, action.Name);
            }
        }
    }

    /// <summary>The analytical tools are reads like the rest; the ones loading every project's money count against the per-question limit.</summary>
    [Fact]
    public void The_analytical_tools_only_read_and_the_heavy_ones_are_marked()
    {
        string[] analytical = ["compare_work", "compare_money", "review_project", "get_company_summary", "summarize_changes", "get_attention"];
        var tools = Tools.Where(t => analytical.Contains(t.Name)).ToList();

        tools.Select(t => t.Name).ShouldBe(analytical, ignoreOrder: true);
        tools.ShouldAllBe(t => !t.Endpoint.Writes);
        // UI tools only ask the app to open what the user may already open: reads too, never a write
        string[] ui = ["open_record", "open_page", "open_surface", "set_preference", "offer_choices", "suggest_replies", "refresh_page", "present_data"];
        Tools.Where(t => ui.Contains(t.Name)).Select(t => t.Name).ShouldBe(ui, ignoreOrder: true);
        Tools.Where(t => ui.Contains(t.Name)).ShouldAllBe(t => !t.Endpoint.Writes && !t.Heavy);
        Tools.Where(t => t.Heavy).Select(t => t.Name)
            .ShouldBe(["compare_money", "get_money_by_month", "review_project", "get_company_summary", "get_attention",
                // a file, a discussion or the website's visitors: each can be large, or asks Google
                "read_project_file", "read_discussion", "get_website_analytics"], ignoreOrder: true);
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
        // descriptions name period values exactly as the schema spells them
        Tools.Single(t => t.Name == "summarize_changes").InputSchema.ToJsonString().ShouldContain("\"last7_days\"");
    }

    /// <summary>
    /// Pay, rates, admin salaries (and the net profit that reveals them), bank details and secrets never reach the model:
    /// not in what tools return, nor in proposal previews and stored arguments (which go back to the model), nor in UI
    /// commands.
    /// </summary>
    [Fact]
    public void No_tool_output_can_carry_the_excluded_fields()
    {
        string[] excluded =
        [
            "HourlyRate", "StandardHourlyRate", "LabourCost", "LabourByPerson", "CostPerHour", "AdminSalaries",
            "UnconvertedAdminSalaries", "NetProfit", "CurrentPay", "Pay", "PayHistory", "Salary", "BankAccount",
            "BankName", "Iban", "Swift", "PasswordHash", "Token", "ApiKey",
        ];
        // A site crew's daily pay is a site cost, already shown to anyone who sees the project (decision 13).
        // Whether someone's time away is paid is a yes or no the user gives, never an amount.
        string[] payAllowed = ["DailyPay", "PayGap"];
        string[] namespaces = ["ID_Indetails.api.Proton.Tools", "ID_Indetails.api.Proton.Actions", "ID_Indetails.api.Proton.Ui"];
        var outputs = typeof(IProtonTool).Assembly.GetTypes()
            .Where(t => namespaces.Contains(t.Namespace) && !typeof(IProtonTool).IsAssignableFrom(t) && !t.Name.EndsWith("Input")
                && !t.Name.Contains('<'));

        var properties = new HashSet<string>();
        var seen = new HashSet<Type>();
        foreach (var type in outputs)
        {
            Collect(type, properties, seen);
        }

        properties.Intersect(excluded).ShouldBeEmpty();
        // any other pay field ("BasePay", "PayRate"…) needs a decision too: "Pay" as a word, not "Payment"
        properties.Where(p => Regex.IsMatch(p, "Pay(?![a-z])") && !payAllowed.Contains(p)).ShouldBeEmpty();
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
}
