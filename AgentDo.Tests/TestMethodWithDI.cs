using AgentDo.OpenAI.Like;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Reflection;

namespace AgentDo.Tests
{
	[TestClass]
	public class TestMethodWithDI : TestMethodAttribute
	{
		private static ServiceProvider? serviceProvider;

		[AssemblyInitialize]
		public static void AssemblyInitialize(TestContext _)
		{
			var builder = new ConfigurationBuilder();
			builder.SetBasePath(Directory.GetCurrentDirectory());
			builder.AddJsonFile("./appsettings.json");
			builder.AddJsonFile("./appsettings.local.json", optional: true);
			builder.AddEnvironmentVariables();
			var config = builder.Build();

			var services = new ServiceCollection();
			services.AddLogging();

			//Bedrock
			services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
				awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
				awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
				region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));

			//OpenAI
			services.AddSingleton(sp => new ChatClient(
				model: "gpt-4o",
				apiKey: config["OPENAI_API_KEY"]!));

			//Local
			services.AddHttpClient("local", c => c.BaseAddress = new Uri("http://localhost:1234/")).AddAsKeyed();
			services.Configure<OpenAILikeClient.Options>("local", o =>
			{
				o.ParallelToolCalls = false;
				//o.Model = "hermes-3-llama-3.2-3b";
				o.Model = "hermes-2-pro-mistral-7b";
			});
			services.AddKeyedTransient("local", (sp, key) => new OpenAILikeClient(
				http: sp.GetRequiredKeyedService<HttpClient>(key),
				options: Options.Create(sp.GetRequiredService<IOptionsMonitor<OpenAILikeClient.Options>>().Get(key!.ToString()))));

			serviceProvider = services.BuildServiceProvider();
		}

		[AssemblyCleanup]
		public static void AssemblyCleanup()
		{
			serviceProvider?.Dispose();
		}

		public override TestResult[] Execute(ITestMethod testMethod)
		{
			var nParameters = testMethod.ParameterTypes?.Length ?? 0;
			if (nParameters != 0)
			{
				var serviceProvider = TestMethodWithDI.serviceProvider;
				using (var scope = serviceProvider!.CreateScope())
				{
					var injectedArgs = testMethod.ParameterTypes!
						.Select(p => scope.ServiceProvider.GetRequiredKeyedService(p.ParameterType, p.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key))
						.ToArray();

					return [testMethod.Invoke(injectedArgs)];
				}
			}
			else
			{
				return base.Execute(testMethod);
			}
		}
	}
}
