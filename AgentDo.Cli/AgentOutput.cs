using Spectre.Console;
using System.Text.Json;

namespace AgentDo.Cli
{
	public static class AgentOutput
	{
		public static Events Events(bool streaming = false)
		{
			return new Events
			{
				BeforeMessage = (role, message) => { AnsiConsole.Markup($"[gray]{role}:[/] "); return Task.CompletedTask; },
				OnMessageDelta = (role, message) => { AnsiConsole.Markup(message); return Task.CompletedTask; },
				AfterMessage = (role, message) => { AnsiConsole.MarkupLine(streaming ? string.Empty : $"[gray]{role}:[/] {message}"); return Task.CompletedTask; },
				BeforeToolCall = (role, tool, toolUse, context, parameters) =>
				{
					AnsiConsole.MarkupLine($"[gray]{role}:[/] [cyan]🛠️{tool.Name}({Markup.Escape(JsonSerializer.Serialize(parameters))})...[/]");
					return Task.CompletedTask;
				},
				AfterToolCall = (role, tool, toolUse, context, result) =>
				{
					AnsiConsole.MarkupLine($"[gray]{toolUse.ToolUseId}: {Markup.Escape(JsonSerializer.Serialize(result))}[/]");
					return Task.CompletedTask;
				},
			};
		}
	}
}
