using System.ClientModel;
using System.Reflection;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

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

			services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
				awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
				awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
				region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));

			services.AddSingleton(sp => new ChatClient(model: "gpt-4o", apiKey: config["OPENAI_API_KEY"]!));

			services.AddKeyedSingleton("hermespro", (sp, _) => new ChatClient(
				model: "hermes-3-llama-3.2-3b", 
				credential: new ApiKeyCredential("none"), 
				options: new OpenAIClientOptions { Endpoint = new Uri("http://127.0.0.1:1234") } ));

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
