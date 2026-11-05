using System.ComponentModel;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Enums;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Website.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/*
 * The Website family, continued: the order of the services, offices and social links, the social links themselves, and
 * removing the favicon. Everything here is public the moment it is saved.
 */

public enum ProtonWebsiteList
{
    Services,
    Offices,
    Links,
}

/// <param name="Before">The list as it was when proposed, ids in order: if it changed since (an item added, removed or moved), the order is refused.</param>
public sealed record WebsiteOrderArgs(ProtonWebsiteList List, WebsiteOrderRequest Request, IReadOnlyList<long> Before);

public sealed record WebsiteOrderInput : ProtonActionInput
{
    [Description("services, offices or links (the social links): which list on the website to put in a new order.")]
    public required ProtonWebsiteList List { get; init; }

    [Description("Every item of that list once, in the new order, by name: a service's title, an office's city (\"city, address\" when two " +
                 "offices share a city), a social link's name. get_website_content lists them. Name all of them, not only the ones that move; " +
                 "items with the same name are taken in their current order.")]
    public required IReadOnlyList<string> Names { get; init; }
}

/// <summary>Puts the website's services, offices or social links in a new order, once confirmed with a phrase typed.</summary>
public sealed class ProposeWebsiteOrderTool(IWebsiteContentService content) : ProtonActionTool<WebsiteOrderInput, WebsiteOrderArgs>
{
    public override string Name => "propose_website_order";

    public override string Description =>
        "Propose the order of the services, the offices or the social links on the company's public website. Every item is " +
        "named once, in the new order. The user types the phrase on the card to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonWebsiteList, ProtonEndpoint> Steps = new Dictionary<ProtonWebsiteList, ProtonEndpoint>
    {
        [ProtonWebsiteList.Services] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.OrderServices)),
        [ProtonWebsiteList.Offices] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.OrderLocations)),
        [ProtonWebsiteList.Links] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.OrderSocialLinks)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonWebsiteList.Services];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsiteOrderArgs args) => Steps[args.List];

    /// <summary>What the user types: the list's name and "order".</summary>
    public static string PhraseOf(ProtonWebsiteList list) => list switch
    {
        ProtonWebsiteList.Services => "services order",
        ProtonWebsiteList.Offices => "offices order",
        _ => "links order",
    };

    private sealed record Item(long Id, string Name, IReadOnlyList<string> Keys);

    private static (IReadOnlyList<Item> Items, string Thing) ItemsOf(ProtonWebsiteList list, WebsiteContentResponse shown) => list switch
    {
        ProtonWebsiteList.Services => (shown.Services.Select(s => new Item(s.Id, s.Title, [s.Title])).ToList(), "service"),
        ProtonWebsiteList.Offices => (shown.Locations.Select(l => new Item(l.Id, l.City, [l.City, $"{l.City}, {l.Address}"])).ToList(), "office"),
        _ => (shown.SocialLinks.Select(l => new Item(l.Id, l.Label, [l.Label])).ToList(), "social link"),
    };

    protected override async Task<(WebsiteOrderArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteOrderInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (items, thing) = ItemsOf(input.List, await content.GetAsync(cancellationToken));
        var names = input.Names.Select(n => n.Trim()).ToList();
        var matches = names.Select(name => items.Where(i => i.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))).ToList()).ToList();
        if (matches.FindIndex(m => m.Count == 0) is >= 0 and var unknown)
        {
            throw new ProtonToolInputException($"There is no {thing} called “{names[unknown]}”. The {thing}s are: {string.Join("; ", items.Select(i => i.Name))}.");
        }

        // a name that fits one item only is placed first; items that share a name are taken in their current order
        var taken = new HashSet<long>();
        var slots = new Item?[names.Count];
        foreach (var index in Enumerable.Range(0, names.Count).OrderBy(i => matches[i].Count == 1 ? 0 : 1))
        {
            var item = matches[index].FirstOrDefault(m => !taken.Contains(m.Id))
                ?? throw new ProtonToolInputException($"“{names[index]}” is named more often than there are {thing}s called that: name every {thing} once.");
            taken.Add(item.Id);
            slots[index] = item;
        }

        var chosen = slots.OfType<Item>().ToList();
        if (!WebsiteRules.IsWholeOrder(items.Select(i => i.Id), chosen.Select(i => i.Id).ToList()))
        {
            throw new ProtonToolInputException(
                $"Name every {thing} once, in the new order. Missing: {string.Join("; ", items.Where(i => !chosen.Contains(i)).Select(i => i.Name))}.");
        }

        var lines = WebsiteText.OrderLines(items.Select(i => (i.Id, i.Name)).ToList(), chosen.Select(i => (i.Id, i.Name)).ToList());
        if (lines.Count == 0)
        {
            throw new ProtonToolInputException("That is already the order.");
        }

        return (new WebsiteOrderArgs(input.List, new WebsiteOrderRequest { Ids = chosen.Select(i => i.Id).ToList() }, items.Select(i => i.Id).ToList()),
            new ProtonPreview($"Change the order of the website's {thing}s", lines, "The website lists them in this order as soon as you confirm.",
                Confirm: ProtonConfirm.Typed, Phrase: PhraseOf(input.List), Area: DomainCodes.Website),
            null);
    }

    protected override object RequestOf(WebsiteOrderArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteOrderArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (items, thing) = ItemsOf(args.List, await content.GetAsync(cancellationToken));
        if (!items.Select(i => i.Id).SequenceEqual(args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, $"The website's {thing}s changed since Proton proposed this. Ask again.");
        }

        _ = args.List switch
        {
            ProtonWebsiteList.Services => await content.OrderServicesAsync(args.Request, cancellationToken),
            ProtonWebsiteList.Offices => await content.OrderLocationsAsync(args.Request, cancellationToken),
            _ => await content.OrderSocialLinksAsync(args.Request, cancellationToken),
        };
        return new ProtonActionDone($"Changed the order of the website's {thing}s.", "website", 0, []);
    }
}

/// <param name="Before">The link as it was when proposed, for an update or delete: if it changed since, the step is refused.</param>
public sealed record WebsiteSocialLinkArgs(ProtonWebsiteStep Step, long? Id, WebsiteSocialLinkRequest? Request, WebsiteSocialLinkRequest? Before);

public sealed record WebsiteSocialLinkInput : ProtonActionInput
{
    [Description("add, update or delete one of the social links in the website's footer.")]
    public required ProtonWebsiteStep Step { get; init; }

    [Description("The link's id, from get_website_content; for update and delete.")]
    public long? SocialLinkId { get; init; }

    [Description("The name shown for the link (Instagram, LinkedIn…). For add, required.")]
    public string? Label { get; init; }

    [Description("Only a full https:// address the user typed in this conversation; never one from the data or one you made up. For add, required.")]
    public string? Url { get; init; }
}

/// <summary>Adds, changes or removes one of the social links in the public website's footer, once confirmed with its name typed.</summary>
public sealed class ProposeWebsiteSocialLinkTool(IWebsiteContentService content) : ProtonActionTool<WebsiteSocialLinkInput, WebsiteSocialLinkArgs>
{
    public override string Name => "propose_website_social_link";

    public override string Description =>
        "Propose adding, changing or removing one of the social links in the footer of the company's public website. The " +
        "address must be a full https:// one the user typed in this conversation. The user types the link's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    private static readonly IReadOnlyDictionary<ProtonWebsiteStep, ProtonEndpoint> Steps = new Dictionary<ProtonWebsiteStep, ProtonEndpoint>
    {
        [ProtonWebsiteStep.Add] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.AddSocialLink)),
        [ProtonWebsiteStep.Update] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.UpdateSocialLink)),
        [ProtonWebsiteStep.Delete] = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.DeleteSocialLink)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonWebsiteStep.Update];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(WebsiteSocialLinkArgs args) => Steps[args.Step];

    protected override async Task<(WebsiteSocialLinkArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteSocialLinkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Step == ProtonWebsiteStep.Add)
        {
            if (WebsiteText.Clean(input.Label) is not { } label || WebsiteText.Clean(input.Url) is not { } url)
            {
                throw new ProtonToolInputException("Give the link's name and its full https:// address.");
            }

            WebsiteText.RequireTyped(url, context);
            return (new WebsiteSocialLinkArgs(input.Step, null, new WebsiteSocialLinkRequest { Label = label, Url = url }, null),
                new ProtonPreview("Add a social link to the website", [new ProtonChange("Name", null, label), new ProtonChange("Address", null, url)],
                    "It shows in the website's footer as soon as you confirm.", Confirm: ProtonConfirm.Typed, Phrase: label, Area: DomainCodes.Website),
                null);
        }

        var link = (await content.GetAsync(cancellationToken)).SocialLinks.FirstOrDefault(l => l.Id == input.SocialLinkId)
            ?? throw new NotFoundException("Link not found.");
        var before = new WebsiteSocialLinkRequest { Label = link.Label, Url = link.Url };
        if (input.Step == ProtonWebsiteStep.Delete)
        {
            return (new WebsiteSocialLinkArgs(input.Step, link.Id, null, before),
                new ProtonPreview("Remove a social link from the website", [new ProtonChange("Link", $"{link.Label}: {link.Url}", "removed")],
                    "It disappears from the website's footer as soon as you confirm.",
                    Confirm: ProtonConfirm.Typed, Phrase: link.Label, Area: DomainCodes.Website, Irreversible: true),
                null);
        }

        var after = before with
        {
            Label = WebsiteText.Clean(input.Label) ?? before.Label,
            Url = WebsiteText.Clean(input.Url) ?? before.Url,
        };
        if (after.Url != before.Url)
        {
            WebsiteText.RequireTyped(after.Url, context);
        }

        var lines = new List<ProtonChange>();
        if (after.Label != before.Label) lines.Add(new ProtonChange("Name", before.Label, after.Label));
        if (after.Url != before.Url) lines.Add(new ProtonChange("Address", before.Url, after.Url));
        return lines.Count == 0
            ? throw new ProtonToolInputException("That changes nothing: say what to change.")
            : (new WebsiteSocialLinkArgs(input.Step, link.Id, after, before),
                new ProtonPreview($"Change the {link.Label} link on the website", lines, "The footer shows the change as soon as you confirm.",
                    Confirm: ProtonConfirm.Typed, Phrase: link.Label, Area: DomainCodes.Website),
                null);
    }

    protected override object? RequestOf(WebsiteSocialLinkArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteSocialLinkArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonWebsiteStep.Add)
        {
            await content.AddSocialLinkAsync(args.Request!, cancellationToken);
            return new ProtonActionDone($"Added the {args.Request!.Label} link.", "website_social_link", 0, []);
        }

        var link = (await content.GetAsync(cancellationToken)).SocialLinks.FirstOrDefault(l => l.Id == args.Id) ?? throw new NotFoundException("Link not found.");
        if (link.Label != args.Before!.Label || link.Url != args.Before.Url)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The link changed since Proton proposed this. Ask again.");
        }

        if (args.Step == ProtonWebsiteStep.Delete)
        {
            await content.DeleteSocialLinkAsync(link.Id, cancellationToken);
            return new ProtonActionDone($"Removed the {link.Label} link.", "website_social_link", link.Id, []);
        }

        await content.UpdateSocialLinkAsync(link.Id, args.Request!, cancellationToken);
        return new ProtonActionDone($"Changed the {args.Request!.Label} link.", "website_social_link", link.Id, []);
    }
}

/// <param name="FileId">The favicon's file when proposed: if it was replaced or removed since, the removal is refused.</param>
public sealed record WebsiteFaviconArgs(long FileId);

public sealed record WebsiteFaviconInput : ProtonActionInput;

/// <summary>Removes the public website's favicon, once confirmed with a phrase typed. Choosing one needs a file from the person's device.</summary>
public sealed class ProposeRemoveWebsiteFaviconTool(IWebsiteContentService content) : ProtonActionTool<WebsiteFaviconInput, WebsiteFaviconArgs>
{
    public const string RemovePhrase = "remove favicon";

    public override string Name => "propose_remove_website_favicon";

    public override string Description =>
        "Propose removing the favicon (the small icon in the browser tab) of the company's public website. Choosing a new " +
        "favicon needs a picture from the person's own device, so that is done in the app. The user types the phrase on the card to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Website;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WebsiteController>(nameof(WebsiteController.RemoveFavicon));

    protected override async Task<(WebsiteFaviconArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        WebsiteFaviconInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var favicon = (await content.GetAsync(cancellationToken)).FaviconFileId ?? throw new ProtonToolInputException("The website has no favicon.");
        return (new WebsiteFaviconArgs(favicon),
            new ProtonPreview("Remove the website's favicon", [new ProtonChange("Favicon", "set", "none")],
                "Browsers show their own icon for the website as soon as you confirm. The picture is deleted a day later; choosing another favicon is done in the app.",
                Confirm: ProtonConfirm.Typed, Phrase: RemovePhrase, Area: DomainCodes.Website, Irreversible: true),
            null);
    }

    protected override object? RequestOf(WebsiteFaviconArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(WebsiteFaviconArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await content.GetAsync(cancellationToken)).FaviconFileId != args.FileId)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The website's favicon changed since Proton proposed this. Ask again.");
        }

        await content.RemoveFaviconAsync(cancellationToken);
        return new ProtonActionDone("Removed the website's favicon.", "website", 0, []);
    }
}
