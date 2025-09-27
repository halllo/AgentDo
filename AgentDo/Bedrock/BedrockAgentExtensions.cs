using Amazon.BedrockRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDo.Bedrock
{
	public static class BedrockAgentExtensions
	{
		public static IAgent AsAgent(this IAmazonBedrockRuntime bedrock, ILoggerFactory loggerFactory, string modelId)
		{
			return new BedrockAgent(
				bedrock: bedrock,
				logger: loggerFactory.CreateLogger<BedrockAgent>(),
				options: Options.Create(new BedrockAgentOptions
				{
					ModelId = modelId,
					Temperature = 0.0F
				}));
		}
	}
}
