# AgentDo
Light-weight function-calling abstraction.

Setup the Amazon Bedrock Runtime and the `BedrockAgent`:

```csharp
services.AddSingleton<IAmazonBedrockRuntime>(sp =>
{
	return new AmazonBedrockRuntimeClient(
		awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
		awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
		region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!));
});

services.AddKeyedSingleton<IAgent, BedrockAgent>("bedrock");
services.Configure<BedrockAgentOptions>(o =>
{
	o.ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";
});
```

Now you can get the agent injected and start it with `agent.Do(...)` by passing in the task and tools.

```csharp
public async Task StartAgent([FromKeyedServices("bedrock")] IAgent agent)
{
	await agent.Do(
		task: "Get the most popular song played on a radio station RGBG and rate it as bad.",
		tools:
		[
			Tool.From([Description("Get radio song")]
				([Description("The call sign for the radio station for which you want the most popular song."), Required] string sign)
				=> new { songName = "Random Song 1" }),

			Tool.From([Description("Rate a song")]
				(string song, string rating)
				=> "Rated!"),
		]);
}
```
