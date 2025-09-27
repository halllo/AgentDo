using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolContextTest
	{
		[TestMethodWithDI]
		public async Task ContextArgumentIsIgnored(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory, "anthropic.claude-3-5-sonnet-20240620-v1:0");
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: (string argument, Tool.Context context) =>
					{
						fCalls.Add($"f({argument})");
					}),
				]);

			Console.WriteLine(JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task EndingAgentLoopViaContext(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory, "anthropic.claude-3-5-sonnet-20240620-v1:0");
			var messages = await agent.Do(
				task: "Call f1 and then f2, one after the other, each with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f1", tool: (string argument, Tool.Context context) =>
					{
						fCalls.Add($"f1({argument})");
						context.Cancelled = true;
					}),
					Tool.From(toolName: "f2", tool: (string argument, Tool.Context context) =>
					{
						fCalls.Add($"f2({argument})");
					}),
				]);

			Console.WriteLine(JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
			CollectionAssert.AreEqual(expected: new[] { "f1(hello)" }, actual: fCalls);
		}
	}
}
