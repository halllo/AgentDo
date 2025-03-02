namespace AgentDo
{
	public class Message
	{
		public string Role { get; }
		public string Text { get; }
		public ToolCall[]? ToolCalls { get; }
		public ToolResult[]? ToolResults { get; }

		internal Message(string role, string text, ToolCall[]? toolCalls, ToolResult[]? toolResults)
		{
			Role = role;
			Text = text;
			ToolCalls = toolCalls;
			ToolResults = toolResults;
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
	}
}
