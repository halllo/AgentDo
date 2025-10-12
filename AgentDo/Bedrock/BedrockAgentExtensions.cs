using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDo.Bedrock
{
	public static class BedrockAgentExtensions
	{
		public static IAgent AsAgent(this IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory, string modelId, Action<BedrockAgentOptions>? configure = null)
		{
			var options = new BedrockAgentOptions
			{
				ModelId = modelId,
				ReasoningBudget = null,
				Temperature = 0.0F
			};

			configure?.Invoke(options);

			return new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(options));
		}
	}
}
