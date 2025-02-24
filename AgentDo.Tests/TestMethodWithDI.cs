using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
			services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
				awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
				awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
				region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));
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
				var injectedArgs = new object[nParameters];
				var serviceProvider = TestMethodWithDI.serviceProvider;
				using (var scope = serviceProvider!.CreateScope())
				{
					for (int i = 0; i < nParameters; i++)
					{
						var parameterType = testMethod.ParameterTypes![i].ParameterType;
						injectedArgs[i] = scope.ServiceProvider.GetService(parameterType)!;
					}
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
