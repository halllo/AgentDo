using AgentDo;
using AgentDo.Bedrock;
using AgentDo.Content;
using Amazon.BedrockRuntime;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Client;
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

app.MapGet("/tools", async () =>
{
	try
	{
		var http = new HttpClient();
		await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(new()
		{
			Name = "Vibe MCP Server",
			Endpoint = new Uri("http://localhost:5253/"),
			TransportMode = HttpTransportMode.StreamableHttp,
			//OAuth = new()
			//{
			//	ClientName = $"ProtectedMcpClient_{DateTime.Now:yyyyMMddHHmmss}",
			//	RedirectUri = new Uri("http://localhost:1179/callback"),
			//	AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
			//	Scopes = ["openid", "profile", "verification", "notes", "admin"],
			//},
		}, http));
		var tools = await mcpClient.ListToolsAsync();
		return Results.Ok(new { tools = tools.Select(t => new { t.Name }) });
	}
	catch (HttpRequestException e)
	{
		return Results.BadRequest($"Could not connect tools: {e.StatusCode}");
	}
});

AgentResult? agentResult = null;
app.MapGet("/history", () => Results.Ok(new { agentResult?.Messages }));

app.MapPost("/generate", async (
	[FromServices] ILogger<Program> logger,
	[FromKeyedServices("bedrock")] IAgent agent,
	HttpContext context,
	[FromBody] GenerateRequest request) =>
{
	var response = context.Response;
	response.Headers.Append("Content-Type", "text/event-stream");
	async Task stream(string chunk)
	{
		logger.LogInformation("Streaming {chunk}", JsonSerializer.Serialize(chunk));
		await response.WriteAsync(chunk);
		await response.Body.FlushAsync();
	}

	if (string.IsNullOrWhiteSpace(request.Query))
	{
		response.StatusCode = 400;
		await stream("Invalid query.");
	}
	else
	{
		var stopwatch = Stopwatch.StartNew();
		logger.LogInformation("Generating response to '{@query}'", request.Query);
		var result = await agent.Do(
			task: new Prompt(request.Query, agentResult),
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
		agentResult = result;
	}
});

app.Run();

record GenerateRequest(string Query);