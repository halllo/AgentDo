using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace AgentDo
{
	public static class JsonSchemaExtensions
	{
		internal readonly static JsonSchemaExporterOptions ExporterOptions = new()
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = (context, schema) =>
			{
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

		public static object? As(this JsonNode? json, Type type)
		{
			if (json != null)
			{
				var t = json.Deserialize(type, DeserializationOptions);
				return t;
			}
			else
			{
				return null;
			}
		}
	}
}
