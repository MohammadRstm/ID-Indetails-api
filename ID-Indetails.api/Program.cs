using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Hubs;
using ID_Indetails.api.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddHosting(builder.Configuration, builder.Environment);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
// First, so everything after sees the visitor's address and scheme, not the proxy's (production only).
if (HostingServiceCollectionExtensions.BehindProxy(app.Configuration))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseCors(ClientCorsOptions.PolicyName);

app.UseAuthentication();

// After authentication so the global limit can count per signed-in user.
app.UseRateLimiter();

app.UseAuthorization();

app.UseMiddleware<LastSeenMiddleware>();

// Sends a successful request's live signals once its changes are committed.
app.UseMiddleware<LiveUpdatesMiddleware>();

app.MapControllers();
// For the container platform, on the private network: nginx doesn't serve it publicly.
app.MapHealthChecks(HostingServiceCollectionExtensions.HealthPath).AllowAnonymous().DisableRateLimiting();
app.MapHub<LiveHub>(LiveHub.Path);

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
