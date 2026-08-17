using System.Text.Json.Nodes;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests
{
	/// <summary>
	/// System.Text.Json's schema exporter emits in-document "$ref" pointers for recursive types.
	/// Those pointers are relative to the type's own schema root, and ToolUsing embeds the schema
	/// underneath a parameter name before sending it - which moves that root. A surviving pointer
	/// would then dangle, or (for "#") silently resolve to the enclosing tool envelope and tell
	/// the model something false. So no reference may escape ToJsonSchema.
	/// </summary>
	[TestClass]
	public sealed class JsonSchemaRefTest
	{
		record Node(string Name, Node? Child = null);
		record Tree(string Name, List<Tree> Children);
		record MutualA(string Name, MutualB? B = null);
		record MutualB(string Name, MutualA? A = null);
		record Described(string Name, [property: Description("who this one reports to")] Described? Boss = null);

		record Artist(string Name);
		record Song(Artist Artist, string Title);
		record Album(Artist Artist, Song[] Songs);

		[TestMethod]
		[DynamicData(nameof(RecursiveTypes))]
		public void RecursiveTypeIsSelfContained(Type type)
		{
			var schema = type.ToJsonSchemaString();

			Assert.DoesNotContain("$ref", schema, $"{type.Name} leaked a pointer that will not resolve once nested.");
			Assert.DoesNotContain("$defs", schema);
		}

		public static IEnumerable<object[]> RecursiveTypes =>
		[
			[typeof(Node)],
			[typeof(Tree)],
			[typeof(MutualA)],
			[typeof(Described)],
		];

		[TestMethod]
		public void RecursionIsTruncatedToATypedPlaceholder()
		{
			// The recursion has to stop somewhere. What is left standing still has to say what
			// shape it is, otherwise the model is told nothing at all about that property.
			var schema = JsonNode.Parse(typeof(Node).ToJsonSchemaString())!;

			var deepest = schema["properties"]!["child"]!;
			while (deepest["properties"] != null) deepest = deepest["properties"]!["child"]!;

			Assert.IsNotNull(deepest["type"], "The truncated placeholder lost its type.");
			Assert.AreEqual("[\"object\",\"null\"]", deepest["type"]!.ToJsonString());
		}

		[TestMethod]
		public void DescriptionSurvivesTruncation()
		{
			// "description" is the only keyword the exporter puts next to a "$ref". Losing it
			// where the recursion is cut would quietly drop documentation the caller wrote.
			var schema = typeof(Described).ToJsonSchemaString();

			Assert.DoesNotContain("$ref", schema);
			Assert.Contains("who this one reports to", schema);
		}

		[TestMethod]
		public void RepeatedTypeIsSpelledOutEverywhereItOccurs()
		{
			// Artist occurs directly and again inside Song. Both have to be fully written out.
			var schema = typeof(Album).ToJsonSchemaString();

			Assert.DoesNotContain("$ref", schema);
			Assert.AreEqual(2, CountOccurrences(schema, "\"name\":{\"type\":\"string\"}"));
		}

		[TestMethod]
		[DynamicData(nameof(RecursiveTypes))]
		public void ToolSchemaContainsNoReference(Type type)
		{
			// The schema that actually goes on the wire is the nested one, not the bare type
			// schema - this is the assertion that matters.
			var tool = ToolFor(type);
			var definition = ToolUsing.GetToolDefinition(tool);
			var schema = definition.Schema.RootElement.GetRawText();

			Assert.DoesNotContain("$ref", schema);
		}

		[TestMethod]
		public void ExceedingTheNodeBudgetFailsLoudly()
		{
			// Silently truncating an over-large schema would produce something wrong in a way
			// nobody can see. Registering the tool has to fail instead.
			var original = JsonSchemaExtensions.MaxInlinedSchemaNodes;
			try
			{
				JsonSchemaExtensions.MaxInlinedSchemaNodes = 3;

				var thrown = Assert.ThrowsExactly<NotSupportedException>(() => typeof(Node).ToJsonSchemaString());
				Assert.Contains(nameof(JsonSchemaExtensions.MaxInlinedSchemaNodes), thrown.Message);
				Assert.Contains("Node", thrown.Message);
			}
			finally
			{
				JsonSchemaExtensions.MaxInlinedSchemaNodes = original;
			}
		}

		[TestMethod]
		public void TypesWithoutReferencesAreUnaffectedByTheBudget()
		{
			// The budget only accounts for reference expansion; a schema with no references is
			// returned as the exporter produced it and must not be able to trip it.
			var original = JsonSchemaExtensions.MaxInlinedSchemaNodes;
			try
			{
				JsonSchemaExtensions.MaxInlinedSchemaNodes = 1;
				var schema = typeof(Album).ToJsonSchemaString();
				Assert.Contains("songs", schema);
			}
			finally
			{
				JsonSchemaExtensions.MaxInlinedSchemaNodes = original;
			}
		}

		private static Tool ToolFor(Type type)
		{
			// One tool per recursive type, built through the same path a caller would use.
			if (type == typeof(Node)) return Tool.From((Node n) => "ok", toolName: "takeNode");
			if (type == typeof(Tree)) return Tool.From((Tree t) => "ok", toolName: "takeTree");
			if (type == typeof(MutualA)) return Tool.From((MutualA a) => "ok", toolName: "takeMutual");
			if (type == typeof(Described)) return Tool.From((Described d) => "ok", toolName: "takeDescribed");
			throw new ArgumentOutOfRangeException(nameof(type), type, "No tool for this type.");
		}

		private static int CountOccurrences(string haystack, string needle)
		{
			int count = 0, index = 0;
			while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
			{
				count++;
				index += needle.Length;
			}
			return count;
		}
	}
}
