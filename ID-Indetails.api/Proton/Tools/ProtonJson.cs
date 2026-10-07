using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// How tool inputs and outputs look to the model: snake_case names and enum values, unknown input fields refused,
/// and each input record's JSON schema (with its [Description]s) generated from the record itself.
/// </summary>
public static class ProtonJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static readonly ConcurrentDictionary<Type, JsonObject> Schemas = new();

    public static JsonObject SchemaOf(Type inputType)
        => (JsonObject)Schemas.GetOrAdd(inputType, BuildSchema).DeepClone();

    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.MakeReadOnly();
        return options;
    }

    private static JsonObject BuildSchema(Type inputType)
    {
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var description = (context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type)
                    .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault()?.Description;
                if (description is not null && node is JsonObject schema)
                {
                    schema["description"] = description;
                }

                return node;
            },
        };

        return (JsonObject)Options.GetJsonSchemaAsNode(inputType, exporter);
    }
}
