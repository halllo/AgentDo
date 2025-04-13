using System.Text.Json;

namespace AgentDo
{
	public class Message
	{
		public string Role { get; }
		public string Text { get; }
		public ToolCall[]? ToolCalls { get; }
		public ToolResult[]? ToolResults { get; }
		public GenerationData? Generation { get; }

		public string GetTextualRepresentation()
		{
			var toolCalls = string.Join("\n", (ToolCalls ?? []).Select(t => JsonSerializer.Serialize(t)));
			var toolResults = string.Join("\n", (ToolResults ?? []).Select(t => JsonSerializer.Serialize(t)));
			return $"{Text}\n{toolCalls}{toolResults}".Trim();
		}

		internal Message(string role, string text, ToolCall[]? toolCalls = null, ToolResult[]? toolResults = null, GenerationData? generationData = null)
		{
			Role = role;
			Text = text;
			ToolCalls = toolCalls;
			ToolResults = toolResults;
			Generation = generationData;
		}

		public class ToolCall
		{
			public string Name { get; }
			public string Id { get; }
			public string Input { get; }

			internal ToolCall(string name, string id, string input)
			{
				Name = name;
				Id = id;
				Input = input;
			}
		}

		public class ToolResult
		{
			public string Id { get; }
			public string Output { get; }

			internal ToolResult(string id, string output)
			{
				Id = id;
				Output = output;
			}
		}

		public class GenerationData
		{
			public TimeSpan Duration { get; set; }
			public int InputTokens { get; }
			public int OutputTokens { get; }

			internal GenerationData(TimeSpan duration, int inputTokens, int outputTokens)
			{
				Duration = duration;
				InputTokens = inputTokens;
				OutputTokens = outputTokens;
			}
		}
	}
}
