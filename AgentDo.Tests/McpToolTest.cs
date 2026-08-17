using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AgentDo.Tests
{
	/// <summary>
	/// AgentDo integrates MCP through exactly one seam: <c>Tool.From(AIFunction)</c>, because
	/// ModelContextProtocol's McpClientTool derives from AIFunction. Both the CLI's chat verb and
	/// AgentDo.Web hand their McpClientTool instances straight to that overload, and no test has
	/// ever covered it. These run server-free, against the same seam.
	/// </summary>
	[TestClass]
	public sealed class McpToolTest
	{
		record Place(string City, string? Street = null);

		private static async Task<object?> Invoke(Tool tool, string input = "{}") =>
			(await ToolUsing.Use(
				tool,
				new ToolUsing.ToolUse { ToolUseId = "t1", ToolName = tool.Name, ToolInput = input },
				role: "assistant",
				context: null!,
				events: null,
				logger: null)).Item1!.Result;

		[TestMethod]
		public async Task ContentReturningToolIsUnwrapped()
		{
			// MCP tools return Microsoft.Extensions.AI content, not plain values. Serialized
			// reflectively that shows the model "$type"/"Annotations" noise with the payload
			// double-encoded as an escaped string.
			var tool = Tool.From(AIFunctionFactory.Create(
				() => new TextContent("""{"city":"Berlin","tempC":21.5}"""),
				name: "getWeather"));

			var json = JsonSerializer.Serialize(await Invoke(tool));

			Assert.DoesNotContain("$type", json);
			Assert.DoesNotContain("Annotations", json);
			Assert.DoesNotContain("AdditionalProperties", json);
			Assert.AreEqual("""{"city":"Berlin","tempC":21.5}""", json);
		}

		[TestMethod]
		public async Task NonJsonContentStaysPlainText()
		{
			var tool = Tool.From(AIFunctionFactory.Create(() => new TextContent("cloudy, 21 degrees"), name: "getWeather"));

			Assert.AreEqual("cloudy, 21 degrees", await Invoke(tool));
		}

		[TestMethod]
		public async Task SeveralContentBlocksAreJoined()
		{
			var tool = Tool.From(AIFunctionFactory.Create(
				() => new List<AIContent> { new TextContent("first"), new TextContent("second") },
				name: "listThings"));

			var result = await Invoke(tool);

			Assert.AreEqual("first\nsecond", result);
			Assert.DoesNotContain("$type", JsonSerializer.Serialize(result));
		}

		[TestMethod]
		public async Task PlainReturnValuesAreUntouched()
		{
			// The common (non-MCP) case has to keep working exactly as before.
			var tool = Tool.From(AIFunctionFactory.Create(() => new { City = "Berlin", TempC = 21.5 }, name: "getWeather"));

			Assert.AreEqual("""{"city":"Berlin","tempC":21.5}""", JsonSerializer.Serialize(await Invoke(tool)));
		}

		[TestMethod]
		public async Task ArgumentsReachTheFunction()
		{
			Place? booked = null;
			var tool = Tool.From(AIFunctionFactory.Create(
				(Place place) => { booked = place; return "booked"; },
				name: "bookPlace",
				description: "Books a place."));

			await Invoke(tool, """{"place":{"city":"Karlsruhe"}}""");

			Assert.IsNotNull(booked);
			Assert.AreEqual("Karlsruhe", booked!.City);
			Assert.IsNull(booked.Street);
		}

		[TestMethod]
		public void NameDescriptionAndServerSchemaAreForwarded()
		{
			// An MCP server owns its schema; AgentDo must not re-derive one by reflection.
			var aiFunction = AIFunctionFactory.Create(
				(Place place) => "booked",
				name: "bookPlace",
				description: "Books a place.");

			var definition = ToolUsing.GetToolDefinition(Tool.From(aiFunction));

			Assert.AreEqual("bookPlace", definition.Name);
			Assert.AreEqual("Books a place.", definition.Description);
			Assert.Contains("city", definition.Schema.RootElement.GetRawText());
		}

		[TestMethod]
		public async Task ParameterlessToolIsInvokable()
		{
			// Plenty of MCP tools take no arguments; the model then sends "{}".
			var called = false;
			var tool = Tool.From(AIFunctionFactory.Create(() => { called = true; return "pong"; }, name: "ping"));

			// A plain return value arrives as a JsonElement - untouched pass-through, as before.
			Assert.AreEqual("\"pong\"", JsonSerializer.Serialize(await Invoke(tool)));
			Assert.IsTrue(called);
		}
	}
}
