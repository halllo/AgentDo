using AgentDo.OpenAI.Like;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Local
{
	[TestClass]
	public sealed class ToolUse3Test
	{
		record Person(
			[property: Description("The full name of the person.")]
			string Name,

			[property: Description("The age of the person at the current day. If it is not directly provided, calculcate it by using the 'calculate_age' tool.")]
			int Age,

			[property: Description("Where the person lives.")]
			Address? Address = null);

		record Address(string City, string? Street = null);

		[TestMethodWithDI]
		public async Task HermesProAgentMultiToolUse([FromKeyedServices("local")] OpenAILikeClient client, ILoggerFactory loggerFactory)
		{
			var agent = new OpenAILikeAgent(
				client: client,
				logger: loggerFactory.CreateLogger<OpenAILikeAgent>(),
				options: Options.Create(new OpenAILikeAgentOptions
				{
					IgnoreInvalidSchema = true,
					IgnoreUnkownTools = true,
					SystemPrompt = @"Answer the user's request using relevant tools (if they are available). 
First, think about which of the provided tools is the relevant tool to answer the user's request. 
Second, go through each of the required parameters of the relevant tool and determine if the user has directly provided or given enough information to infer a value. 
When deciding if the parameter can be inferred, carefully consider all the context including the return values from other tools to see if it supports optaining a specific value.
If all of the required parameters are present or can be reasonably inferred, close the thinking tag and proceed with the tool call.
BUT, if one of the values for a required parameter is missing, DO NOT invoke the function (not even with fillers for the missing params) and instead ask the user to provide the missing parameters. 
DO NOT ask for more information on optional parameters if it is not provided."
				}));
//Before calling a tool, do some analysis within <thinking></thinking> tags.

			Person? registeredPerson = default;
			var messages = await agent.Do(
				task: "I would like to register Manuel Naujoks (born on September 7th in 1986) from Karlsruhe.",
				tools:
				[
					Tool.From(toolName: "register_person", tool: [Description("Register person.")] (Person person) =>
					{
						registeredPerson = person;
						return "registered";
					}),

					Tool.From(toolName: "get_today", tool: [Description("Gets the current day, month and year.")]() => "01 March 2025"),

					Tool.From(toolName: "calculate_age", tool: [Description("Calculate age.")](DateTime birthday) => (DateTime.Today - birthday).TotalDays / 365),
				]);

			Console.WriteLine(JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
			Assert.IsNotNull(registeredPerson);
			Assert.AreEqual("Manuel Naujoks", registeredPerson.Name);
			Assert.AreEqual(38, registeredPerson.Age);
			Assert.IsNotNull(registeredPerson.Address);
			Assert.AreEqual("Karlsruhe", registeredPerson.Address!.City);
			Assert.IsNull(registeredPerson.Address!.Street);
		}
	}
}
