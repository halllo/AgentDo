using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ReturnValueTest
	{
		[TestMethodWithDI]
		public async Task AsyncObject(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: async (string argument) =>
					{
						fCalls.Add($"f({argument})");
						await Task.Delay(TimeSpan.FromSeconds(2));
						return new { argument };
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task SyncObject(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: (string argument) =>
					{
						fCalls.Add($"f({argument})");
						return new { argument };
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task AyncPrimitive(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: async (string argument) =>
					{
						fCalls.Add($"f({argument})");
						await Task.Delay(TimeSpan.FromSeconds(1));
						return argument;
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task SyncPrimitive(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: (string argument) =>
					{
						fCalls.Add($"f({argument})");
						return argument;
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task AyncVoid(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: async (string argument) =>
					{
						fCalls.Add($"f({argument})");
						await Task.Delay(TimeSpan.FromSeconds(1));
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}

		[TestMethodWithDI]
		public async Task SyncVoid(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var fCalls = new List<string>();

			var agent = bedrock.AsAgent(loggerFactory);
			var messages = await agent.Do(
				task: "Call f with argument 'hello'.",
				tools:
				[
					Tool.From(toolName: "f", tool: (string argument) =>
					{
						fCalls.Add($"f({argument})");
					}),
				]);

			CollectionAssert.AreEqual(expected: new[] { "f(hello)" }, actual: fCalls);
		}
	}
}
