namespace AgentDo.Bedrock
{
	public class BedrockAgentOptions
	{
		public string? ModelId { get; set; }
		public float? Temperature { get; set; }
		public bool LogTask { get; set; }
		public bool Streaming { get; set; }
		public int? ReasoningBudget { get; set; }
		public string? SystemPrompt { get; set; }
	}
}
