using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Extensions;

public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Controllers, JSON conventions, validation and the centralized error handling that
    /// gives every failure the same response shape.
    /// </summary>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers(options =>
            {
                options.Filters.Add<ValidationFilter>();
                // FluentValidation owns "required" messages, not MVC's implicit attributes.
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)))
            .ConfigureApiBehaviorOptions(options =>
                // Malformed JSON and unbindable values use the same error shape as validation.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value!.Errors
                                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage)
                                .ToArray());

                    var body = ApiErrorWriter.Build(
                        context.HttpContext,
                        ErrorType.Validation,
                        ErrorCodes.ValidationFailed,
                        "The request body is invalid.",
                        errors);

                    return new BadRequestObjectResult(body);
                });

        // Responses written outside MVC (errors from the exception handler, authorization and
        // rate limiting) use these options: same enum format as controller responses.
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));

        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        var cors = configuration.GetSection(ClientCorsOptions.SectionName).Get<ClientCorsOptions>() ?? new();
        services.AddCors(options => options.AddPolicy(ClientCorsOptions.PolicyName, policy => policy
            .WithOrigins(cors.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return services;
    }
}
