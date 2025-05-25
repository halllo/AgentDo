using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace AgentDo
{
	public static class JsonSchemaExtensions
	{
		internal readonly static JsonSchemaExporterOptions ExporterOptions = new()
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = (context, schema) =>
			{
				// Render converter properties as strings.
				var renderAsString = context.PropertyInfo?.PropertyType.GetCustomAttribute<ConvertFromStringAttribute>(inherit: true) != null;
				if (renderAsString)
				{
					var nullableStringSchema = new JsonObject
					{
						["type"] = new JsonArray("string", "null"),
						["default"] = null,
					};
					schema = context.PropertyInfo!.IsSetNullable ? nullableStringSchema : typeof(string).ToJsonSchema();
				}

				// Determine if a type or property and extract the relevant attribute provider
				ICustomAttributeProvider? attributeProvider = context.PropertyInfo is not null
					? context.PropertyInfo.AttributeProvider
					: context.TypeInfo.Type;

				// Look up any description attributes
				DescriptionAttribute? descriptionAttr = attributeProvider?
					.GetCustomAttributes(inherit: true)
					.Select(attr => attr as DescriptionAttribute)
					.FirstOrDefault(attr => attr is not null);

				// Apply description attribute to the generated schema
				if (descriptionAttr != null)
				{
					if (schema is not JsonObject jObj)
					{
						// Handle the case where the schema is a boolean
						JsonValueKind valueKind = schema.GetValueKind();
						Debug.Assert(valueKind is JsonValueKind.True or JsonValueKind.False);
						schema = jObj = new JsonObject();
						if (valueKind is JsonValueKind.False)
						{
							jObj.Add("not", true);
						}
					}

					// Put it directly after the type property
					var typeIndex = jObj.IndexOf("type");
					jObj.Insert(typeIndex + 1, "description", descriptionAttr.Description);
				}

				return schema;
			}
		};

		internal readonly static JsonSerializerOptions GenerationOptions = new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		internal readonly static JsonSerializerOptions OutputOptions = new(JsonSerializerOptions.Default)
		{
			WriteIndented = false,
		};

		internal readonly static JsonSerializerOptions DeserializationOptions = new(JsonSerializerOptions.Default)
		{
			PropertyNameCaseInsensitive = true
		};

		public static string JsonSchemaString<T>() => typeof(T).ToJsonSchemaString();
		public static string ToJsonSchemaString(this Type type, string? description = null)
		{
			var schema = type.ToJsonSchema(description);
			var schemaString = schema.ToJsonString(OutputOptions);
			return schemaString;
		}
		public static JsonNode ToJsonSchema(this Type type, string? description = null)
		{
			var schema = GenerationOptions.GetJsonSchemaAsNode(type, ExporterOptions);
			if (!string.IsNullOrWhiteSpace(description))
			{
				var schemaObject = schema.AsObject();
				var typeIndex = schemaObject.IndexOf("type");
				schemaObject.Insert(typeIndex + 1, "description", description);
			}
			return schema;
		}

		internal static object? As(this JsonNode? json, Type type, AutoDiscoverConverters? autoDiscoverConverters = null)
		{
			var deserializationOptions = autoDiscoverConverters != null
				? DeserializationOptions.WithConverters(GetAutoDiscoveredConverters(type, autoDiscoverConverters))
				: DeserializationOptions;

			return As(json, type, deserializationOptions);
		}

		internal static IEnumerable<JsonConverter> GetAutoDiscoveredConverters(Type type, AutoDiscoverConverters? autoDiscoverConverters = null)
		{
			autoDiscoverConverters ??= new AutoDiscoverConverters();
			autoDiscoverConverters.CollectRecursivelyFrom(type);
			return autoDiscoverConverters.GetConverters();
		}

		internal static JsonSerializerOptions WithConverters(this JsonSerializerOptions options, IEnumerable<JsonConverter> converters)
		{
			var newOptions = new JsonSerializerOptions(options);
			foreach (var converter in converters)
			{
				newOptions.Converters.Add(converter);
			}

			return newOptions;
		}

		public static object? As(this JsonNode? json, Type type, JsonSerializerOptions options)
		{
			if (json != null)
			{
				var t = json.Deserialize(type, options);
				return t;
			}
			else
			{
				return null;
			}
		}

		public static T? As<T>(this JsonDocument? json, params JsonConverter[] converters)
		{
			var deserializationOptions = converters.Length > 0
				? DeserializationOptions.WithConverters(converters)
				: DeserializationOptions;

			var t = json.As<T>(deserializationOptions);
			return t;
		}

		public static T? As<T>(this JsonDocument? json, JsonSerializerOptions options)
		{
			if (json != null)
			{
				var t = json.Deserialize<T>(options);
				return t;
			}
			else
			{
				return default;
			}
		}
	}
}
