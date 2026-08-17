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
			// The same configuration the [Requires*] conditions evaluate, so a test can never be
			// scheduled to run against settings its condition did not see.
			var config = TestEnvironment.Configuration;

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
				c.BaseAddress = TestEnvironment.LocalLlmBaseAddress;
				c.Timeout = TimeSpan.FromMinutes(5);
			}).AddAsKeyed();
			services.Configure<OpenAILikeClient.Options>("local", o =>
			{
				o.ParallelToolCalls = false;
				o.Model = TestEnvironment.LocalLlmModel;
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
