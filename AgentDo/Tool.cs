using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentDo
{
	public class Tool
	{
		public string Name { get; private set; }

		public string? Description { get; private set; }

		public Delegate Delegate { get; private set; }

		public bool RequireApproval { get; private set; }

		public bool LogInputsAndOutputs { get; private set; }

		internal JsonDocument? Schema { get; private set; }

		private Tool(string name, string? description, Delegate tool, bool logInputsAndOutputs, bool requireApproval, JsonDocument? schema)
		{
			this.Name = name;
			this.Description = description;
			this.Delegate = tool;
			this.LogInputsAndOutputs = logInputsAndOutputs;
			this.RequireApproval = requireApproval;
			this.Schema = schema;
		}

		public static Tool From(Delegate tool, [CallerArgumentExpression(nameof(tool))] string toolName = "", bool logInputsAndOutputs = true, bool requireApproval = false)
		{
			string actualToolName = GetToolName(tool, toolName);
			return new Tool(actualToolName, null, tool, logInputsAndOutputs, requireApproval, null);
		}

		public static Tool From(JsonDocument schema, Action<JsonDocument> tool, [CallerArgumentExpression(nameof(tool))] string toolName = "") => From(schema, (Delegate)tool, toolName);
		public static Tool From<T>(JsonDocument schema, Func<JsonDocument, T> tool, [CallerArgumentExpression(nameof(tool))] string toolName = "") => From(schema, (Delegate)tool, toolName);
		public static Tool From(JsonDocument schema, Func<JsonDocument, Task> tool, [CallerArgumentExpression(nameof(tool))] string toolName = "") => From(schema, (Delegate)tool, toolName);
		public static Tool From<T>(JsonDocument schema, Func<JsonDocument, Task<T>> tool, [CallerArgumentExpression(nameof(tool))] string toolName = "") => From(schema, (Delegate)tool, toolName);
		public static Tool From(JsonDocument schema, Delegate tool, string toolName = "", bool logInputsAndOutputs = false)
		{
			string actualToolName = GetToolName(tool, toolName);
			return new Tool(actualToolName, null, tool, logInputsAndOutputs, false, schema);
		}

		public static Tool From(AIFunction aiFunction, bool logInputsAndOutputs = true, bool requireApproval = false)
		{
			return new Tool(
				name: aiFunction.Name,
				description: aiFunction.Description,
				tool: async (JsonObject i) =>
				{
					var arguments = i.Deserialize<Dictionary<string, object?>>();
					var result = await aiFunction.InvokeAsync(new AIFunctionArguments(arguments)).ConfigureAwait(false);
					return UnwrapContent(result);
				},
				logInputsAndOutputs: logInputsAndOutputs,
				requireApproval: requireApproval,
				schema: JsonDocument.Parse(aiFunction.JsonSchema.ToString()));
		}

		/// <summary>
		/// MCP tools - and anything else built on Microsoft.Extensions.AI content - hand back
		/// <see cref="AIContent"/> objects rather than plain values. Serializing one of those
		/// reflectively shows the model "$type", "Annotations" and "AdditionalProperties" noise,
		/// with the actual payload double-encoded as an escaped string. Unwrap it to what the tool
		/// really returned. Plain values already arrive as JsonElement and are passed through.
		/// </summary>
		private static object? UnwrapContent(object? result)
		{
			switch (result)
			{
				case TextContent text:
					return AsJsonIfPossible(text.Text);

				case IEnumerable<AIContent> contents:
					var texts = contents.OfType<TextContent>().Select(c => c.Text).ToList();
					// Nothing textual in there (images, audio); leave it for the caller to handle.
					if (texts.Count == 0) return result;
					return texts.Count == 1 ? AsJsonIfPossible(texts[0]) : string.Join("\n", texts);

				default:
					return result;
			}
		}

		/// <summary>
		/// MCP tools conventionally return their payload as a JSON string. Handing the model a
		/// parsed object beats handing it the same thing escaped inside a string.
		/// </summary>
		private static object AsJsonIfPossible(string text)
		{
			try
			{
				return JsonDocument.Parse(text).RootElement.Clone();
			}
			catch (JsonException)
			{
				return text;
			}
		}

		private static string GetToolName(Delegate tool, string toolName)
		{
			string actualToolName;
			if (toolName.Contains(' ') || toolName.Contains('.'))
			{
				var displayName = tool.GetMethodInfo().GetCustomAttribute<DescriptionAttribute>()?.Description;
				if (string.IsNullOrWhiteSpace(displayName))
				{
					throw new NotSupportedException("Tool needs a proper name. Use toolName or DescriptionAttribute.");
				}
				actualToolName = Regex.Replace(displayName!, @"[^a-zA-Z0-9]+(.)", m => m.Groups[1].Value.ToUpper()).Trim(' ').Trim('.');
			}
			else
			{
				actualToolName = toolName;
			}

			return actualToolName;
		}

		public class Context
		{
			private IReadOnlyList<Message> messages;

			internal Context(IReadOnlyList<Message> messages)
			{
				this.messages = messages;
			}

			public bool Cancelled { get; set; }
			public bool RememberToolResultWhenCancelled { get; set; }
			public string? Text { get; internal set; }
			public string? Reason { get; internal set; }

			public IReadOnlyList<Message> GetMessages() => this.messages;

			public void Suspend()
			{
				Cancelled = true;
				RememberToolResultWhenCancelled = false;
			}
		}
	}
}
