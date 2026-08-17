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
			var generated = GenerationOptions.GetJsonSchemaAsNode(type, ExporterOptions);

			// The exporter emits a "$ref" pointer for recursive types and for types that repeat
			// outside a property position. Tool input schemas are consumed by models that do not
			// resolve those pointers, so inline them into self-contained schemas.
			var schema = InlineReferences(generated, type);

			if (!string.IsNullOrWhiteSpace(description))
			{
				var schemaObject = schema.AsObject();
				var typeIndex = schemaObject.IndexOf("type");
				schemaObject.Insert(typeIndex + 1, "description", description);
			}
			return schema;
		}

		/// <summary>
		/// Upper bound on the number of nodes an inlined schema may contain. Inlining is
		/// multiplicative, so a deeply nested type whose sub-objects repeat can grow by orders of
		/// magnitude - past what fits in a model's context window, and re-billed on every turn.
		/// Raise it if a schema is legitimately this large.
		/// </summary>
		public static int MaxInlinedSchemaNodes { get; set; } = 10_000;

		/// <summary>
		/// Replaces every "$ref" pointer with a copy of the node it points at, so that the schema
		/// is self-contained. Where that is impossible - a recursive type, or a pointer that does
		/// not resolve - the reference is replaced by a permissive placeholder rather than left in
		/// place. It cannot be left in place: the exporter's pointers are relative to the schema's
		/// own root, and <see cref="ToolUsing"/> embeds this schema underneath a parameter name
		/// before sending it, which moves that root. A surviving pointer would then either dangle
		/// or, in the case of "#", silently resolve to the enclosing tool envelope.
		/// </summary>
		internal static JsonNode InlineReferences(JsonNode schema, Type? type = null)
		{
			// The exporter only emits pointers for recursion and for types repeated outside a
			// property position, so most schemas have none and need no rebuilding at all.
			if (!ContainsReference(schema)) return schema;

			return Inline(schema, new InlineContext(schema, MaxInlinedSchemaNodes, type))!;
		}

		private sealed class InlineContext
		{
			public InlineContext(JsonNode root, int limit, Type? type)
			{
				Root = root;
				Limit = limit;
				Budget = limit;
				Type = type;
			}

			public readonly JsonNode Root;
			public readonly HashSet<string> Expanding = new HashSet<string>();
			private readonly int Limit;
			private readonly Type? Type;
			private int Budget;

			/// <summary>Accounts for one emitted node, and fails loudly once the budget is gone.
			/// Silently truncating would produce a schema that is wrong in a way nobody can see.</summary>
			public void Spend()
			{
				if (--Budget >= 0) return;
				throw new NotSupportedException(
					$"Inlining the JSON schema{(Type == null ? "" : $" of '{Type}'")} exceeded {Limit} nodes. "
					+ $"Nested types that repeat multiply when they are inlined. Flatten the type, or raise "
					+ $"{nameof(JsonSchemaExtensions)}.{nameof(MaxInlinedSchemaNodes)} if the schema really is this large.");
			}
		}

		private static bool ContainsReference(JsonNode? node)
		{
			switch (node)
			{
				case JsonObject obj: return obj.ContainsKey("$ref") || obj.Any(p => ContainsReference(p.Value));
				case JsonArray array: return array.Any(ContainsReference);
				default: return false;
			}
		}

		private static JsonNode? Inline(JsonNode? node, InlineContext context)
		{
			context.Spend();

			switch (node)
			{
				case JsonObject obj:
					if (TryGetReference(obj, out var pointer))
					{
						var target = ResolveJsonPointer(context.Root, pointer!);

						// A pointer already being expanded means the type is recursive; one that does
						// not resolve is broken. Neither can be inlined, and neither may survive.
						if (target == null || context.Expanding.Contains(pointer!))
						{
							return Truncate(obj, target);
						}

						context.Expanding.Add(pointer!);
						var inlined = Inline(target, context);
						context.Expanding.Remove(pointer!);

						return WithSiblingsOf(inlined, obj);
					}

					var copy = new JsonObject();
					foreach (var property in obj)
					{
						copy[property.Key] = Inline(property.Value, context);
					}
					return copy;

				case JsonArray array:
					var items = new JsonArray();
					foreach (var item in array)
					{
						items.Add(Inline(item, context));
					}
					return items;

				default:
					return node?.DeepClone();
			}
		}

		/// <summary>
		/// Stands in for a reference that cannot be inlined. Keeps the target's "type" so the
		/// placeholder still says what shape to expect, and stays self-contained.
		/// </summary>
		private static JsonNode Truncate(JsonObject reference, JsonNode? target)
		{
			var truncated = new JsonObject();
			if ((target as JsonObject)?["type"] is JsonNode type)
			{
				truncated["type"] = type.DeepClone();
			}
			return WithSiblingsOf(truncated, reference);
		}

		/// <summary>
		/// Applies the keywords sitting next to a "$ref" on top of the node it resolved to. The
		/// exporter only ever emits "description" there, and a local annotation wins.
		/// </summary>
		private static JsonNode WithSiblingsOf(JsonNode? inlined, JsonObject reference)
		{
			// Nothing but the "$ref" itself, so there is nothing to merge.
			if (reference.Count <= 1) return inlined ?? new JsonObject();

			var merged = inlined as JsonObject;
			if (merged == null)
			{
				// A boolean schema has nowhere to put the siblings; promote it to an object,
				// the same way the exporter transform above does.
				merged = new JsonObject();
				if (inlined != null && inlined.GetValueKind() == JsonValueKind.False)
				{
					merged.Add("not", true);
				}
			}

			foreach (var property in reference)
			{
				if (property.Key == "$ref") continue;
				merged[property.Key] = property.Value?.DeepClone();
			}
			return merged;
		}

		private static bool TryGetReference(JsonObject obj, out string? pointer)
		{
			pointer = null;
			return obj.TryGetPropertyValue("$ref", out var reference)
				&& reference is JsonValue value
				&& value.TryGetValue(out pointer)
				&& pointer != null;
		}

		private static JsonNode? ResolveJsonPointer(JsonNode root, string pointer)
		{
			if (pointer == "#") return root;
			// Ordinal: a culture-sensitive comparison lets ignorable code points through, after
			// which Substring(2) would slice the wrong characters.
			if (!pointer.StartsWith("#/", StringComparison.Ordinal)) return null;

			var current = root;
			foreach (var rawSegment in pointer.Substring(2).Split('/'))
			{
				// JSON pointer escaping: "~1" is "/" and "~0" is "~" (in that order).
				var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
				switch (current)
				{
					case JsonObject obj when obj.TryGetPropertyValue(segment, out var child):
						current = child;
						break;
					case JsonArray array when int.TryParse(segment, out var index) && index >= 0 && index < array.Count:
						current = array[index];
						break;
					default:
						return null;
				}
				if (current == null) return null;
			}
			return current;
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
