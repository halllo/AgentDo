using Amazon.Runtime.Documents;
using Amazon.Runtime.Documents.Internal.Transform;
using Amazon.Runtime.Internal.Transform;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using ThirdParty.Json.LitJson;

namespace AgentDo
{
	public static class JsonSchemaExtensions
	{
		static JsonSchemaExporterOptions exporterOptions = new()
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

		static JsonSerializerOptions generationOptions = new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		static JsonSerializerOptions outputOptions = new(JsonSerializerOptions.Default)
		{
			WriteIndented = false,
		};

		static JsonSerializerOptions deserializationOptions = new(JsonSerializerOptions.Default)
		{
			PropertyNameCaseInsensitive = true
		};

		public static string JsonSchemaString<T>() => typeof(T).ToJsonSchemaString();
		public static string ToJsonSchemaString(this Type type, string? description = null)
		{
			var schema = type.ToJsonSchema(description);
			var schemaString = schema.ToJsonString(outputOptions);
			return schemaString;
		}
		public static JsonNode ToJsonSchema(this Type type, string? description = null)
		{
			var schema = generationOptions.GetJsonSchemaAsNode(type, exporterOptions);
			if (!string.IsNullOrWhiteSpace(description))
			{
				var schemaObject = schema.AsObject();
				var typeIndex = schemaObject.IndexOf("type");
				schemaObject.Insert(typeIndex + 1, "description", description);
			}
			return schema;
		}

		public static Document ToAmazonJson(this JsonNode json) => ToAmazonJson(json.ToJsonString(outputOptions));
		public static Document ToAmazonJson(this string json)
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			using var context = new JsonUnmarshallerContext(stream, false, null);
			var unmarshaller = DocumentUnmarshaller.Instance;
			var unmarshalled = unmarshaller.Unmarshall(context);
			return unmarshalled;
		}

		public static string FromAmazonJson(this Document amazonJson)
		{
			if (amazonJson.IsDictionary() || amazonJson.IsList())
			{
				var sb = new StringBuilder();
				var jsonWriter = new JsonWriter(sb);
				DocumentMarshaller.Instance.Write(jsonWriter, amazonJson);
				var marshalled = sb.ToString();
				return marshalled;
			}
			else
			{
				throw new NotSupportedException("Only dictionaries and lists can be marshalled.");
			}
		}

		public static T? FromAmazonJson<T>(this Document amazonJson)
		{
			var json = FromAmazonJson(amazonJson);
			var t = JsonSerializer.Deserialize<T>(json, deserializationOptions);
			return t;
		}

		public static object? As(this JsonNode? json, Type type)
		{
			if (json != null)
			{
				var t = json.Deserialize(type, deserializationOptions);
				return t;
			}
			else
			{
				return null;
			}
		}
	}
}
