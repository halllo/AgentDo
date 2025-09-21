using AgentDo;
using AgentDo.Bedrock;
using AgentDo.Content;
using Amazon.BedrockRuntime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ModelContextProtocol.Client;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Services.AddOpenApi();
//builder.Services.AddCors(options =>
//{
//	options.AddDefaultPolicy(policy =>
//	{
//		policy.WithOrigins("http://localhost:4200")
//			  .AllowAnyHeader()
//			  .AllowAnyMethod();
//	});
//});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(options =>
	{
		options.Cookie.Name = $"AgentDo.Web";
		options.Cookie.SameSite = SameSiteMode.Strict;
		options.ForwardChallenge = OpenIdConnectDefaults.AuthenticationScheme;
		options.Events.OnRedirectToAccessDenied = new Func<RedirectContext<CookieAuthenticationOptions>, Task>(context =>
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			return context.Response.CompleteAsync();
		});
	})
	.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
	{
		o.Authority = "https://localhost:5001";
		o.ClientId = "mcp_server";
		o.ClientSecret = "secret";
		o.ResponseType = OpenIdConnectResponseType.Code;
		o.Scope.Add("openid");
		o.Scope.Add("profile");
		o.Scope.Add("verification");
		o.Scope.Add("notes");
		o.Scope.Add("admin");
		o.SaveTokens = true;
		o.GetClaimsFromUserInfoEndpoint = true;
	});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<SetAccessToken>();
builder.Services.AddHttpClient("mcp").AddHttpMessageHandler<SetAccessToken>().AddAsKeyed();

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
//app.UseCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();


app.MapGet("/account", (HttpContext context) => Results.Ok(context.User.Claims.Select(c => new { c.Type, c.Value }))).RequireAuthorization();


List<SseClientTransportOptions> mcpServers =
[
	new() {
		Name = "Vibe MCP Server",
		Endpoint = new Uri("http://localhost:5253/bot"),
		TransportMode = HttpTransportMode.StreamableHttp,
	}
];
app.MapGet("/tools", async (HttpContext httpContext, IAuthenticationService authN, [FromKeyedServices("mcp")] HttpClient http) =>
{
	List<McpClientTool> tools = [];
	var authNed = await authN.AuthenticateAsync(httpContext, null);
	if (authNed.Succeeded)
	{
		foreach (var server in mcpServers)
		{
			await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(server, http));
			tools.AddRange(await mcpClient.ListToolsAsync());
		}
	}
	return Results.Ok(new { tools = tools.Select(t => new { t.Name, t.Description }) });
});


string agentResult = "null";
app.MapGet("/history", () => Results.Ok(new { Messages = JsonSerializer.Deserialize<AgentResult>(agentResult)?.Messages.Select(m => new { m.Role, m.Text, Time = m.Generation?.GeneratedAt }) }));


app.MapPost("/generate", async (
	[FromServices] ILogger<Program> logger,
	[FromKeyedServices("bedrock")] IAgent agent,
	HttpContext httpContext,
	IAuthenticationService authN,
	[FromKeyedServices("mcp")] HttpClient http,
	[FromBody] GenerateRequest request) =>
{
	var response = httpContext.Response;
	response.Headers.Append("Content-Type", "text/event-stream");
	async Task stream(string chunk, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("Streaming {chunk}", JsonSerializer.Serialize(chunk));
		var serializedChunk = JsonSerializer.SerializeToUtf8Bytes(chunk);//preventing net::ERR_INCOMPLETE_CHUNKED_ENCODING
		await response.Body.WriteAsync(serializedChunk, cancellationToken);
		await response.Body.FlushAsync();
	}

	if (string.IsNullOrWhiteSpace(request.Query))
	{
		response.StatusCode = 400;
		await stream("Invalid query.");
	}
	else
	{
		logger.LogInformation("Loading tools");
		List<IMcpClient> mcpClients = [];
		List<McpClientTool> tools = [];
		var authNed = await authN.AuthenticateAsync(httpContext, null);
		if (authNed.Succeeded)
		{
			foreach (var server in mcpServers)
			{
				var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(server, http));
				mcpClients.Add(mcpClient);
				tools.AddRange(await mcpClient.ListToolsAsync());
			}
		}

		var stopwatch = Stopwatch.StartNew();
		logger.LogInformation("Generating response to '{@query}'", request.Query);
		var result = await agent.Do(
			task: new Prompt(request.Query, JsonSerializer.Deserialize<AgentResult>(agentResult)),
			tools:
			[
				..tools.Where(t => request.UseTools?.Contains(t.Name) ?? false).Select(t => Tool.From(t)),
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
		agentResult = JsonSerializer.Serialize(result);

		foreach (var mcpClient in mcpClients) await mcpClient.DisposeAsync();
	}
});

app.Run();

record GenerateRequest(string Query, string[]? UseTools = null);
