using System.Text.Json;

namespace AgentDo
{
	public class Message
	{
		public string Role { get; set; } = null!;
		public string? Text { get; set; }
		public Reasoning? Reason { get; set; }
		public ToolCall[]? ToolCalls { get; set; }
		public ToolResult[]? ToolResults { get; set; }
		public GenerationData? Generation { get; set; }

		public string GetTextualRepresentation()
		{
			var toolCalls = string.Join("\n", (ToolCalls ?? []).Select(t => JsonSerializer.Serialize(t)));
			var toolResults = string.Join("\n", (ToolResults ?? []).Select(t => JsonSerializer.Serialize(t)));
			return $"{Text}\n{toolCalls}{toolResults}".Trim();
		}

		public Message()
		{
		}

		internal Message(string role, string? text, Reasoning? reason = null, ToolCall[]? toolCalls = null, ToolResult[]? toolResults = null, GenerationData? generationData = null)
		{
			Role = role;
			Text = text;
			Reason = reason;
			ToolCalls = toolCalls;
			ToolResults = toolResults;
			Generation = generationData;
		}

		public class Reasoning
		{
			public string? Text { get; set; }
			public string? Signature { get; set; }
		}

		public class ToolCall
		{
			public string Name { get; set; } = null!;
			public string Id { get; set; } = null!;
			public string Input { get; set; } = null!;
		}

		public class ToolResult
		{
			public string Id { get; set; } = null!;
			public string Output { get; set; } = null!;
		}

		public class GenerationData
		{
			public DateTimeOffset GeneratedAt { get; set; }
			public TimeSpan Duration { get; set; }
			public int? InputTokens { get; set; }
			public int? OutputTokens { get; set; }
		}
	}
}
