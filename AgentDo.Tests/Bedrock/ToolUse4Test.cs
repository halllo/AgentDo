using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	[TestClass]
	public sealed class ToolUse4Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task BedrockAgentMultiToolUseWithApproval(IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory)
		{
			var agent = new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
					Temperature = 0.0F
				}));

			Person? registeredPerson = default;
			var result = await agent.Do(
				task: "I would like to register Manuel Naujoks (born on September 7th in 1986) from Karlsruhe.",
				tools:
				[
					Tool.From([Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}, requireApproval: true),

					Tool.From([Description("Get today.")]() => "01 March 2025"),
				]);

			if (result.NeedsApprovalToContinue)
			{
				Assert.IsNull(registeredPerson);
				result = await result.ApproveAndContinue();
			}
			else
			{
				Assert.Fail("Expected pending approval for tool use, but none was found.");
			}

			Console.WriteLine(JsonSerializer.Serialize(result.Messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
