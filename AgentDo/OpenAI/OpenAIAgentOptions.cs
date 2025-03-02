namespace AgentDo.OpenAI
{
	public class OpenAIAgentOptions
	{
		public float? Temperature { get; set; }
		public bool LogTask { get; set; }
		public string? SystemPrompt { get; set; }
	}
}
