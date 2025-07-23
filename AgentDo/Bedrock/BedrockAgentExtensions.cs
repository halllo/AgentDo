using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDo.Bedrock
{
	public static class BedrockAgentExtensions
	{
		public static IAgent AsAgent(this IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory, string? modelId = null)
		{
			return new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = modelId ?? "anthropic.claude-3-5-sonnet-20240620-v1:0",
					Temperature = 0.0F
				}));
		}
	}
}
