using Microsoft.Extensions.Configuration;
using System.Net.Sockets;

namespace AgentDo.Tests
{
	/// <summary>
	/// Everything the suite needs from the machine it happens to run on. Every member is lazy and
	/// none of them throw, because MSTest evaluates the [Requires*] conditions during discovery -
	/// that is, outside of and possibly before [AssemblyInitialize].
	/// </summary>
	public static class TestEnvironment
	{
		public static IConfiguration Configuration => configuration.Value;
		private static readonly Lazy<IConfiguration> configuration = new(() => new ConfigurationBuilder()
			// AppContext.BaseDirectory rather than the current directory: during discovery the
			// working directory is not guaranteed to be the test output directory.
			.SetBasePath(AppContext.BaseDirectory)
			.AddJsonFile("appsettings.json", optional: true)
			.AddJsonFile("appsettings.local.json", optional: true)
			.AddEnvironmentVariables()
			.Build(),
			LazyThreadSafetyMode.ExecutionAndPublication);

		public static bool HasBedrockCredentials =>
			!string.IsNullOrWhiteSpace(Configuration["AWSBedrockAccessKeyId"])
			&& !string.IsNullOrWhiteSpace(Configuration["AWSBedrockSecretAccessKey"])
			&& !string.IsNullOrWhiteSpace(Configuration["AWSBedrockRegion"]);

		public static bool HasOpenAIKey => !string.IsNullOrWhiteSpace(Configuration["OPENAI_API_KEY"]);

		public static Uri LocalLlmBaseAddress => new(Configuration["LocalLlm:BaseAddress"] ?? "http://localhost:1234/");
		public static string LocalLlmModel => Configuration["LocalLlm:Model"] ?? "gemma-3-27b-it";

		/// <summary>
		/// One TCP probe for the whole run. Without it every local-model test waits out the
		/// five minute HttpClient timeout before failing.
		/// </summary>
		public static bool LocalLlmIsListening => localLlmIsListening.Value;
		private static readonly Lazy<bool> localLlmIsListening = new(() =>
		{
			try
			{
				using var probe = new TcpClient();
				return probe.ConnectAsync(LocalLlmBaseAddress.Host, LocalLlmBaseAddress.Port).Wait(TimeSpan.FromMilliseconds(500))
					&& probe.Connected;
			}
			catch
			{
				return false;
			}
		}, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	/// <summary>
	/// The private sample documents a few tests are written against. They are not in the repository
	/// and never will be - they are personal papers, and the assertions (-565.65 and friends) are
	/// the values printed on them. Point "TestAssets" in appsettings.local.json at wherever they
	/// live; anyone without them gets those tests skipped rather than failed.
	/// </summary>
	public static class TestAssets
	{
		public const string CreditCardStatementPdf = "CreditCardStatementPdf";
		public const string CreditCardStatementPng = "CreditCardStatementPng";
		public const string InvoicePng = "InvoicePng";

		/// <summary>Absolute path of the asset, or null when unconfigured or missing on disk.</summary>
		public static string? Path(string asset)
		{
			var configured = TestEnvironment.Configuration[$"TestAssets:{asset}"];
			if (string.IsNullOrWhiteSpace(configured)) return null;

			var expanded = Environment.ExpandEnvironmentVariables(configured!);
			if (!System.IO.Path.IsPathRooted(expanded))
			{
				// Relative entries resolve against "TestAssets:Directory", so the common case is
				// one directory plus a few file names.
				var directory = TestEnvironment.Configuration["TestAssets:Directory"];
				expanded = System.IO.Path.Combine(
					string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : Environment.ExpandEnvironmentVariables(directory!),
					expanded);
			}

			var absolute = System.IO.Path.GetFullPath(expanded);
			return System.IO.File.Exists(absolute) ? absolute : null;
		}

		public static bool Available(string asset) => Path(asset) != null;

		/// <summary>
		/// For use inside a test body. Unreachable when the test carries [RequiresAsset], but it
		/// says what is wrong rather than throwing a bare FileNotFoundException if someone forgets.
		/// </summary>
		public static FileInfo File(string asset) => new(Path(asset)
			?? throw new InvalidOperationException(
				$"Sample document '{asset}' is not available. Set \"TestAssets:{asset}\" in AgentDo.Tests/appsettings.local.json."));
	}
}
