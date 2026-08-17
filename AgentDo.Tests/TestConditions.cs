namespace AgentDo.Tests
{
	/// <summary>
	/// Skips - rather than fails - a test whose private sample document is not configured on this
	/// machine. MSTest evaluates ConditionBaseAttribute before the test method runs, so the DI
	/// container is never touched and the outcome is a real Skipped with the reason attached.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class RequiresAssetAttribute : ConditionBaseAttribute
	{
		private readonly string asset;

		public RequiresAssetAttribute(string asset) : base(ConditionMode.Include)
		{
			this.asset = asset;
			IgnoreMessage =
				$"Sample document '{asset}' is not available on this machine. Set \"TestAssets:{asset}\" "
				+ $"in AgentDo.Tests/appsettings.local.json (or the TestAssets__{asset} environment variable).";
		}

		// A distinct group per asset, so stacking two of these ANDs them.
		public override string GroupName => $"{nameof(RequiresAssetAttribute)}:{asset}";
		public override bool IsConditionMet => TestAssets.Available(asset);
	}

	/// <summary>Skips a test when nothing is serving the OpenAI-compatible API locally.</summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class RequiresLocalLlmAttribute : ConditionBaseAttribute
	{
		public RequiresLocalLlmAttribute() : base(ConditionMode.Include)
		{
			IgnoreMessage =
				$"Nothing is listening on {TestEnvironment.LocalLlmBaseAddress}. Start LM Studio and load "
				+ $"'{TestEnvironment.LocalLlmModel}', or point \"LocalLlm:BaseAddress\" somewhere else.";
		}

		public override string GroupName => nameof(RequiresLocalLlmAttribute);
		public override bool IsConditionMet => TestEnvironment.LocalLlmIsListening;
	}

	/// <summary>Skips a test when no AWS Bedrock credentials are configured.</summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class RequiresBedrockAttribute : ConditionBaseAttribute
	{
		public RequiresBedrockAttribute() : base(ConditionMode.Include)
		{
			IgnoreMessage =
				"No AWS Bedrock credentials configured. Set AWSBedrockAccessKeyId, AWSBedrockSecretAccessKey "
				+ "and AWSBedrockRegion in AgentDo.Tests/appsettings.local.json.";
		}

		public override string GroupName => nameof(RequiresBedrockAttribute);
		public override bool IsConditionMet => TestEnvironment.HasBedrockCredentials;
	}

	/// <summary>Skips a test when no OpenAI API key is configured.</summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class RequiresOpenAIAttribute : ConditionBaseAttribute
	{
		public RequiresOpenAIAttribute() : base(ConditionMode.Include)
		{
			IgnoreMessage = "No OPENAI_API_KEY configured in AgentDo.Tests/appsettings.local.json.";
		}

		public override string GroupName => nameof(RequiresOpenAIAttribute);
		public override bool IsConditionMet => TestEnvironment.HasOpenAIKey;
	}

	/// <summary>
	/// Cost traits, so CI can run the free tests without opting into a bill.
	/// </summary>
	public static class TestCategories
	{
		/// <summary>No network at all: free, and fast enough to run on every commit.</summary>
		public const string Offline = "Offline";
		/// <summary>Billed AWS Bedrock calls.</summary>
		public const string Bedrock = "Bedrock";
		/// <summary>Billed OpenAI calls.</summary>
		public const string OpenAI = "OpenAI";
		/// <summary>Free, but needs a local model server.</summary>
		public const string LocalLlm = "LocalLlm";
	}

	/// <summary>
	/// The model ids the suite runs against, in one place. These are consts rather than
	/// configuration because BedrockExtensions.ConverseWithTool takes one as a default parameter
	/// value, which the compiler requires to be constant.
	/// </summary>
	public static class TestModels
	{
		/// <summary>The model everything is written against.</summary>
		public const string Sonnet = "eu.anthropic.claude-sonnet-4-6";

		/// <summary>
		/// Pinned separately: the reasoning and streaming tests assert on the shape of thinking
		/// blocks, so they stay on the model those assertions were captured from. Move them to
		/// <see cref="Sonnet"/> only together with re-recording the assertions.
		/// </summary>
		public const string SonnetWithReasoning = "eu.anthropic.claude-sonnet-4-20250514-v1:0";
	}
}
