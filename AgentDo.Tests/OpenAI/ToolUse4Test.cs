﻿using AgentDo.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.OpenAI
{
	[TestClass]
	public sealed class ToolUse4Test
	{
		record Person(string Name, int Age, Address? Address = null);
		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task OpenAIAgentMultiToolUseWithApproval(ChatClient client, ILoggerFactory loggerFactory)
		{
			var agent = new OpenAIAgent(
				client: client,
				logger: loggerFactory.CreateLogger<OpenAIAgent>(),
				options: Options.Create(new OpenAIAgentOptions
				{
					Temperature = 0.0F,
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
