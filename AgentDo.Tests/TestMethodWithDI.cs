using AgentDo.OpenAI.Like;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AgentDo.Tests
{
	[TestClass]
	public class TestMethodWithDI : TestMethodAttribute
	{
		public TestMethodWithDI([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1) : base(callerFilePath, callerLineNumber)
		{
		}

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
			services.AddHttpClient("local", c =>
			{
				c.BaseAddress = new Uri("http://localhost:1234/");
				c.Timeout = TimeSpan.FromMinutes(5);
			}).AddAsKeyed();
			services.Configure<OpenAILikeClient.Options>("local", o =>
			{
				o.ParallelToolCalls = false;
				//o.Model = "hermes-3-llama-3.2-3b";
				//o.Model = "hermes-2-pro-mistral-7b";
				//o.Model = "llama-3.3-70b-instruct";
				o.Model = "gemma-3-27b-it";
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

		public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
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

					return [await testMethod.InvokeAsync(injectedArgs)];
				}
			}
			else
			{
				return await base.ExecuteAsync(testMethod);
			}
		}
	}
}
