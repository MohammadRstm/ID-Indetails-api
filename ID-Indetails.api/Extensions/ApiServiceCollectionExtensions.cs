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
                // query and route enums by their snake_case names, as the JSON spells them
                options.ModelBinderProviders.Insert(0, new SnakeCaseEnumBinderProvider());
                // FluentValidation owns "required" messages, not MVC's implicit attributes.
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
                // Numbers are JSON numbers, never strings, so the OpenAPI document types them as numbers.
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
                // An explicit null for a property that can't be null is refused, so lists and texts are never null.
                options.JsonSerializerOptions.RespectNullableAnnotations = true;
                // The JSON reader's messages name .NET types and byte positions: BindingErrors says what was wrong instead.
                options.AllowInputFormatterExceptionMessages = false;
            })
            .ConfigureApiBehaviorOptions(options =>
                // Malformed JSON and unbindable values use the same error shape as validation.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = BindingErrors.Of(context.ModelState);
                    // one problem is said as it is ("Amount is too large."), so it reads well wherever it is shown
                    var messages = errors.Values.SelectMany(m => m).Distinct().ToList();

                    var body = ApiErrorWriter.Build(
                        context.HttpContext,
                        ErrorType.Validation,
                        ErrorCodes.ValidationFailed,
                        messages.Count == 1 ? messages[0] : "One or more fields are invalid.",
                        errors);

                    return new BadRequestObjectResult(body);
                });

        // Responses written outside MVC (errors from the exception handler, authorization and
        // rate limiting) use these options: same enum format as controller responses.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.RespectNullableAnnotations = true;
        });

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
