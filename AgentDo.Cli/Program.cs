using AgentDo;
using AgentDo.Bedrock;
using Amazon.Bedrock;
using Amazon.BedrockRuntime;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Reflection;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var host = CreateHostBuilder().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	try
	{
		var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

		var actions = typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttribute(typeof(VerbAttribute)) != null).OrderBy(t => t.Name).ToList();
		var parserResult = Parser.Default.ParseArguments(args, actions.ToArray());
		var parsed = parserResult as Parsed<object>;

		if (parsed != null)
		{
			var action = parsed.Value;
			var actionInvocationMethod = action.GetType().GetMethods().Single(m => !m.IsSpecialName && !m.IsStatic && m.DeclaringType == action.GetType());
			try
			{
				var methodArguments = actionInvocationMethod.GetParameters()
					.Select(p => serviceProvider.GetRequiredKeyedService(p.ParameterType, p.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key))
					.ToArray();

				var result = actionInvocationMethod.Invoke(action, methodArguments);
				if (result is Task t)
				{
					await t;
				}
			}
			catch (TargetInvocationException e)
			{
				logger.LogError(e, "Cannot invoke command.");
			}
			catch (Exception e)
			{
				logger.LogError(e, "Unkown error.");
			}
			return 0;
		}
		else
		{
			return 1;
		}
	}
	catch (Exception e)
	{
		var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
		logger.LogError(e, "Something went wrong.");
		return 1;
	}
}

static IHostBuilder CreateHostBuilder()
{
	return Host.CreateDefaultBuilder()
		.ConfigureAppConfiguration(cfg =>
		{
			cfg.AddJsonFile("appsettings.local.json", optional: true);
		})
		.UseSerilog((ctx, cfg) =>
		{
			cfg.ReadFrom.Configuration(ctx.Configuration);
		})
		.ConfigureServices((ctx, services) =>
		{
			var config = ctx.Configuration;

			services.AddSingleton<IAmazonBedrock>(sp => new AmazonBedrockClient(
				awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
				awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
				region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));

			services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
				awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
				awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
				region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));

			services.AddKeyedSingleton<IAgent, BedrockAgent>("bedrock");
			services.Configure<BedrockAgentOptions>(o =>
			{
				o.ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";
			});
		});
}
