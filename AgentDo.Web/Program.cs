using AgentDo;
using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy =>
	{
		policy.WithOrigins("http://localhost:4200")
			  .AllowAnyHeader()
			  .AllowAnyMethod();
	});
});
builder.Services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
	awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
	awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
	region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));
builder.Services.AddKeyedSingleton<IAgent, BedrockAgent>("bedrock");
builder.Services.Configure<BedrockAgentOptions>(o =>
{
	o.ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";
	o.Streaming = true;
});


var app = builder.Build();
app.MapOpenApi();
app.UseCors();
app.UseHttpsRedirection();

app.MapGet("/generate", async (
	[FromServices] ILogger<Program> logger,
	[FromKeyedServices("bedrock")] IAgent agent,
	HttpContext context,
	[FromQuery] string query) =>
{
	var response = context.Response;
	response.Headers.Append("Content-Type", "text/event-stream");
	async Task stream(string chunk)
	{
        logger.LogInformation("Streaming {chunk}", JsonSerializer.Serialize(chunk));
        await response.WriteAsync(chunk);
		await response.Body.FlushAsync();
	}

	if (string.IsNullOrWhiteSpace(query))
	{
		response.StatusCode = 400;
		await stream("Invalid query.");
	}
	else
	{
		var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Generating response to '{@query}'", query);
		await agent.Do(
			task: query,
			tools:
            [
                Tool.From([Description("Get data and time")]() => $"{DateTime.Now.ToLongDateString()} {DateTime.Now.ToLongTimeString()}")
            ],
			events: new Events
			{
				BeforeMessage = (role, message) => stream($"{role}: "),
				OnMessageDelta = (role, message) => stream(message),
				AfterMessage = (role, message) => stream(""),
				BeforeToolCall = (role, tool, toolUse, context, parameters) => stream($"\n\n{role}: {tool.Name}({JsonSerializer.Serialize(parameters)})\n"),
				AfterToolCall = (role, tool, toolUse, context, result) => stream($"{toolUse.ToolUseId}: {JsonSerializer.Serialize(result)}\n\n"),
			});
		stopwatch.Stop();
        logger.LogInformation("Response generated after {stopwatch}", stopwatch.Elapsed);
    }
});

app.Run();
