using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);
builder.Services.AddApplicationServices();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(ClientCorsOptions.PolicyName);

app.UseAuthentication();

// After authentication so the global limit can count per signed-in user.
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
