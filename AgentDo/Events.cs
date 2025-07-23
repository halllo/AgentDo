namespace AgentDo
{
	public class Events
	{
		public OnMessage? BeforeMessage { get; set; }
		public OnMessage? OnMessageDelta { get; set; }
		public OnMessage? AfterMessage { get; set; }
		public BeforeToolCall? BeforeToolCall { get; set; }
		public AfterToolCall? AfterToolCall { get; set; }
	}

	public delegate void OnMessage(string role, string message);
	public delegate void BeforeToolCall(string role, Tool tool, ToolUsing.ToolUse toolUse, Tool.Context? context, object?[] parameters);
	public delegate void AfterToolCall(string role, Tool tool, ToolUsing.ToolUse toolUse, Tool.Context? context, object? result);
}
