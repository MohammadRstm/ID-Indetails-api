using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>
/// The public website's reads: only published projects, and only what the website shows, never anything internal.
/// Its contact form emails the company's own address.
/// </summary>
public sealed class PublicWebsiteService(
    IWebsiteRepository website,
    ICompanyRepository company,
    IFileRepository files,
    IFileStorage storage,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IClock clock,
    ILogger<PublicWebsiteService> logger) : IPublicWebsiteService
{
    private const string ProjectNotFound = "Project not found.";

    public async Task<IReadOnlyList<PublicWebsiteProject>> ProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await website.ProjectsAsync(publishedOnly: true, cancellationToken);
        var linked = await LinkedAsync(projects, cancellationToken);
        return projects.Select(p => Public(p, linked)).OfType<PublicWebsiteProject>().ToList();
    }

    public async Task<PublicWebsiteProject> ProjectAsync(string slug, CancellationToken cancellationToken = default)
    {
        var project = await website.PublishedProjectAsync(slug, cancellationToken) ?? throw new NotFoundException(ProjectNotFound);
        return Public(project, await LinkedAsync([project], cancellationToken)) ?? throw new NotFoundException(ProjectNotFound);
    }

    public async Task<IReadOnlyList<PublicWebsiteOffering>> ServicesAsync(CancellationToken cancellationToken = default)
        => (await website.ListAsync<WebsiteOffering>(cancellationToken: cancellationToken))
            .Select(s => new PublicWebsiteOffering(s.Id, s.Title, s.Description)).ToList();

    public async Task<IReadOnlyList<PublicWebsiteLocation>> LocationsAsync(CancellationToken cancellationToken = default)
        => (await website.ListAsync<WebsiteLocation>(cancellationToken: cancellationToken))
            .Select(l => new PublicWebsiteLocation(l.Id, l.City, l.Country, l.Latitude, l.Longitude, l.Address, l.Phone)).ToList();

    public async Task<PublicWebsiteContact> ContactAsync(CancellationToken cancellationToken = default)
        => new((await company.GetSettingsAsync(cancellationToken: cancellationToken))?.ContactEmail);

    public async Task<IReadOnlyList<PublicWebsiteSocialLink>> SocialLinksAsync(CancellationToken cancellationToken = default)
        => (await website.ListAsync<WebsiteSocialLink>(cancellationToken: cancellationToken))
            .Select(l => new PublicWebsiteSocialLink(l.Id, l.Label, l.Url)).ToList();

    public async Task<PublicWebsiteSettings> SettingsAsync(CancellationToken cancellationToken = default)
        => new((await company.GetSettingsAsync(cancellationToken: cancellationToken))?.WebsiteFaviconFileId is { } id ? PublicWebsitePaths.File(id) : null);

    public async Task<FileContent> OpenFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        var file = await website.IsPublicFileAsync(fileId, cancellationToken) ? await files.GetAsync(fileId, cancellationToken) : null;
        return file is null
            ? throw new NotFoundException("Picture not found.")
            : new FileContent(storage.OpenRead(file.StorageKey), file.MimeType, file.OriginalName);
    }

    public async Task SendContactMessageAsync(WebsiteContactMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (WebsiteContactRules.IsRobot(request.Website))
        {
            return;
        }

        var to = (await company.GetSettingsAsync(cancellationToken: cancellationToken))?.ContactEmail
            ?? throw new ServiceUnavailableException(ErrorCodes.WebsiteContactUnavailable, "Messages can't be sent right now. Please email us instead.");
        var message = new WebsiteContactMessage(
            request.Name.Trim(), request.Email.Trim(), string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(), request.Message.Trim(), clock.UtcNow);
        try
        {
            await emailSender.SendAsync(WebsiteEmails.ContactMessage(emailOptions.Value.ProductName, to, message), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A website contact message couldn't be emailed.");
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed, "Your message couldn't be sent right now. Please try again in a few minutes.", exception);
        }
    }

    private Task<IReadOnlyDictionary<long, SystemProjectFacts>> LinkedAsync(IEnumerable<WebsiteProject> projects, CancellationToken cancellationToken)
        => website.SystemProjectsAsync(projects.Where(p => p.ProjectId is not null).Select(p => p.ProjectId!.Value).ToList(), cancellationToken);

    /// <summary>The project as the website reads it, or null when it lacks something the website needs.</summary>
    private static PublicWebsiteProject? Public(WebsiteProject p, IReadOnlyDictionary<long, SystemProjectFacts> linked)
    {
        var (status, year) = WebsiteProjectService.Shown(p, p.ProjectId is { } id ? linked.GetValueOrDefault(id) : null);
        if (status is null || year is null || p.City is null || p.Country is null || p.Latitude is null || p.Longitude is null
            || p.Description is null || p.ThumbnailFileId is null || p.HeroFileId is null)
        {
            return null;
        }

        return new PublicWebsiteProject(
            p.Id,
            p.Slug,
            p.Title,
            WebsiteRules.PublicType(p.Type),
            WebsiteRules.PublicStatus(status.Value),
            p.Client ?? string.Empty,
            new PublicPlace(p.City, p.Country, p.Latitude.Value, p.Longitude.Value),
            year.Value,
            p.Description,
            PublicWebsitePaths.File(p.ThumbnailFileId.Value),
            new PublicImage(PublicWebsitePaths.File(p.HeroFileId.Value), p.HeroAlt ?? p.Title, Credit(p.HeroCreditPhotographer, p.HeroCreditSource, p.HeroCreditUrl)),
            p.Images.OrderBy(i => i.Position)
                .Select(i => new PublicImage(PublicWebsitePaths.File(i.FileId), i.Alt, Credit(i.CreditPhotographer, i.CreditSource, i.CreditUrl)))
                .ToList(),
            p.ConveyorOrder is not null,
            p.ConveyorOrder);
    }

    private static PublicImageCredit? Credit(string? photographer, string? source, string? url)
        => photographer is null || source is null || url is null ? null : new PublicImageCredit(photographer, source, url);
}
