using AgentDo.Content;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using static AgentDo.AgentResult;

namespace AgentDo.Bedrock
{
	public class BedrockAgent : IAgent
	{
		private readonly IAmazonBedrockRuntime bedrock;
		private readonly ILogger<BedrockAgent> logger;
		private readonly IOptions<BedrockAgentOptions> options;

		public BedrockAgent(IAmazonBedrockRuntime bedrock, ILogger<BedrockAgent> logger, IOptions<BedrockAgentOptions> options)
		{
			this.bedrock = bedrock;
			this.logger = logger;
			this.options = options;
		}

		public async Task<AgentResult> Do(Prompt task, List<Tool> tools, Events? events = null, CancellationToken cancellationToken = default)
		{
			var pendingToolUses = task.AgentContext?.PendingToolUses;
			var promptPreviousMessages = task.AgentContext?.Messages ?? [];
			var previousUnassociatedToolCalls = promptPreviousMessages.SelectMany(m => m.ToolCalls?.Select(c => new { c.Name, c.Id }) ?? []).Where(tc => !tools.Any(t => t.Name == tc.Name)).ToList();
			bool isUnassociatedTool(string nameOrId) => previousUnassociatedToolCalls.Any(tc => tc.Name == nameOrId || tc.Id == nameOrId);
			var previousMessages = promptPreviousMessages
				.Select(m => new { m, Role = m.Role == ConversationRole.User.Value ? ConversationRole.User : ConversationRole.Assistant, })
				.Select(m => m.m switch
				{
					{ ToolCalls: not null } => m.m.ToolCalls.Any(c => isUnassociatedTool(c.Name))
						? m.Role.Says(m.m.GetTextualRepresentation())
						: m.Role.Says(
							text: m.m.Text,
							reason: m.m.Reason == null ? null : new ReasoningContentBlock { ReasoningText = new ReasoningTextBlock { Text = m.m.Reason.Text, Signature = m.m.Reason.Signature } },
							toolUses: m.m.ToolCalls.Select(c => new ToolUseBlock { ToolUseId = c.Id, Name = c.Name, Input = c.Input.ToAmazonJson() })),
					{ ToolResults: not null } => m.m.ToolResults.Any(tr => isUnassociatedTool(tr.Id))
						? m.Role.Says(m.m.GetTextualRepresentation())
						: m.Role.Says(m.m.ToolResults.Select(r => GetAsToolResultMessage(r.Id, r.Output.ToAmazonJson()))),
					_ => m.Role.Says(m.m.GetTextualRepresentation()),
				})
				// A stored message can rehydrate into nothing at all (no text, no tool calls,
				// no tool results). Bedrock rejects contentless messages, so leave them out.
				.Where(m => m.Content.Count > 0)
				.ToList();

			var images = task.Images.Select(i => i.ForBedrock()).ToList();
			var documents = task.Documents.Select(d => d.ForBedrock()).ToList();

			var resumableToolUses = previousMessages.LastOrDefault()?.ToolsUse()
				.Where(t => !(pendingToolUses?.Uses.Any(p => p.ToolUseId == t.ToolUseId) ?? false))
				.Select(t => new ToolUsing.ToolUse
				{
					ToolUseId = t.ToolUseId,
					ToolName = t.Name,
					ToolInput = t.Input.FromAmazonJson(),
					ToolResult = null,
				})
				.ToList() ?? [];

			var taskMessage = pendingToolUses == null && !resumableToolUses.Any()
				? ConversationRole.User.Says(
					text: task.Text,
					images: images,
					documents: documents)
				: null;

			// Resuming passes an empty prompt, which leaves nothing to send.
			if (taskMessage != null && taskMessage.Content.Count == 0) taskMessage = null;

			if (taskMessage != null)
			{
				if (options.Value.LogTask)
				{
					logger.LogDebug("{Role}: {Text}", taskMessage.Role, taskMessage.Text());
					var eventTask = events?.AfterMessage?.Invoke(taskMessage.Role, taskMessage.Text() ?? string.Empty);
					if (eventTask != null) await eventTask.ConfigureAwait(false);
				}
			}

			var messages = previousMessages.Concat(taskMessage != null ? [taskMessage] : []).ToList();
			var resultMessages = promptPreviousMessages.Concat(taskMessage != null ? [new(taskMessage.Role, taskMessage.Text(), generationData: new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow })] : []).ToList();

			// Resuming passes an empty prompt, so there may be nothing at all to add: no prompt,
			// no tool uses left to resume, nothing pending approval. If the conversation then ends
			// with the assistant's own message, Bedrock rejects it ("the conversation must end with
			// a user message") - the model has already had the last word. Hand back what we have
			// rather than pay for a call that cannot succeed.
			if (taskMessage == null && resumableToolUses.Count == 0 && pendingToolUses == null
				&& messages.LastOrDefault()?.Role?.Value != ConversationRole.User.Value)
			{
				logger.LogDebug("Nothing to do: no prompt, no tool uses to resume and nothing pending.");
				return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
			}

			var toolConfig = tools.Count == 0 ? null : new ToolConfiguration()
			{
				Tools = [.. tools.Select(t => CreateTool(t))]
			};

			var inferenceConfig = new InferenceConfiguration()
			{
				Temperature = options.Value.ReasoningBudget > 0 ? 1.0F : options.Value.Temperature ?? 0.0F
			};

			bool keepConversing = true;
			Tool.Context context = new(resultMessages);
			while (keepConversing)
			{
				var remainingResumableToolUses = resumableToolUses.Where(t => t.ToolResult == null);
				if (remainingResumableToolUses.Any())
				{
					var toolResults = new List<ToolResultBlock>();
					foreach (var resumableToolUse in remainingResumableToolUses)
					{
						var (toolResult, requiresApproval) = await ToolUsing.Use(tools, resumableToolUse, previousMessages.Last().Role, context, events, logger, cancellationToken: cancellationToken).ConfigureAwait(false);
						if (toolResult == null && requiresApproval != null)
						{
							return new AgentResult
							{
								Agent = this,
								Task = task,
								Tools = tools,
								Messages = resultMessages,
								PendingToolUses = pendingToolUses,
							};
						}
						else if (toolResult != null)
						{
							resumableToolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

							var toolResultMessage = GetAsToolResultMessage(resumableToolUse.ToolUseId, toolResult.Result);
							toolResults.Add(toolResultMessage);

							if (context.Cancelled)
							{
								keepConversing = false;
								break;
							}
						}
						else throw new ArgumentException("No tool result and no approval requirement.");
					}

					// No results means a contentless message, which Bedrock rejects.
					if (toolResults.Count > 0 && (!context.Cancelled || context.RememberToolResultWhenCancelled))
					{
						messages.Add(ConversationRole.User.Says(toolResults));
						resultMessages.Add(new Message(ConversationRole.User, "", toolResults: [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
					}

					// Suspending above only broke out of the foreach. Without this the run falls
					// through and converses with an unanswered tool use, which Bedrock rejects.
					if (!keepConversing) break;
				}
				if (pendingToolUses != null)
				{
					var toolResults = pendingToolUses.Uses
						.TakeWhile(t => t.ToolResult != null)
						.Select(toolUse => JsonSerializer.Deserialize<ToolResultBlock>(toolUse.ToolResult!)!)
						.ToList();

					foreach (var toolUse in pendingToolUses.Uses.SkipWhile(t => t.ToolResult != null))
					{
						var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolUse, pendingToolUses.Role, context, events, logger, cancellationToken: cancellationToken).ConfigureAwait(false);

						if (toolResult == null && requiresApproval != null)
						{
							return new AgentResult
							{
								Agent = this,
								Task = task,
								Tools = tools,
								Messages = resultMessages,
								PendingToolUses = pendingToolUses,
							};
						}
						else if (toolResult != null)
						{
							toolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

							var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
							toolResults.Add(toolResultMessage);

							if (context.Cancelled)
							{
								keepConversing = false;
								break;
							}
						}
						else throw new ArgumentException("No tool result and no approval requirement.");
					}

					// No results means a contentless message, which Bedrock rejects.
					if (toolResults.Count > 0 && (!context.Cancelled || context.RememberToolResultWhenCancelled))
					{
						messages.Add(ConversationRole.User.Says(toolResults));
						resultMessages.Add(new Message(ConversationRole.User, "", toolResults: [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
					}
					pendingToolUses = null;
				}
				else
				{
					var converseDurationStopwatch = Stopwatch.StartNew();
					// A blank system prompt is rejected by Bedrock just like a blank text block,
					// and binding an empty config key yields "" rather than null.
					var systemPrompt = string.IsNullOrWhiteSpace(options.Value.SystemPrompt) ? default(List<SystemContentBlock>?) : [new SystemContentBlock { Text = options.Value.SystemPrompt }];
					var reasoningConfig = options.Value.ReasoningBudget > 0 ? Amazon.Runtime.Documents.Document.FromObject(new
					{
						thinking = new Dictionary<string, object>
						{
							{"type", "enabled"},
							{"budget_tokens", options.Value.ReasoningBudget},
						}
					}) : default;

					Amazon.BedrockRuntime.Model.Message responseMessage;
					TokenUsage tokenUsage;
					StopReason stopReason;
					if (options.Value.Streaming)
					{
						// Holds the live HTTP response, so it has to be disposed.
						using var streamResponse = await bedrock.ConverseStreamAsync(new ConverseStreamRequest
						{
							ModelId = options.Value.ModelId ?? throw new ArgumentNullException(nameof(options.Value.ModelId), "No ModelId provided."),
							System = systemPrompt,
							AdditionalModelRequestFields = reasoningConfig,
							Messages = messages,
							ToolConfig = toolConfig,
							InferenceConfig = inferenceConfig,
						}, cancellationToken).ConfigureAwait(false);

						(responseMessage, tokenUsage, stopReason) = await streamResponse.ToMessage(events, cancellationToken: cancellationToken).ConfigureAwait(false);
					}
					else
					{
						var response = await bedrock.ConverseAsync(new ConverseRequest
						{
							ModelId = options.Value.ModelId ?? throw new ArgumentNullException(nameof(options.Value.ModelId), "No ModelId provided."),
							System = systemPrompt,
							Messages = messages,
							AdditionalModelRequestFields = reasoningConfig,
							ToolConfig = toolConfig,
							InferenceConfig = inferenceConfig,
						}, cancellationToken).ConfigureAwait(false);

						responseMessage = response.Output.Message;
						tokenUsage = response.Usage;
						stopReason = response.StopReason;
					}

					//rewind streams in case we want to send them again
					foreach (var stream in Enumerable
						.Concat(images.Select(i => i.Source.Bytes), documents.Select(d => d.Source.Bytes))
						.Where(s => s.CanSeek))
					{
						stream.Seek(0, SeekOrigin.Begin);
					}

					converseDurationStopwatch.Stop();

					// This message goes straight back out with the next request, and Bedrock rejects
					// a ContentBlock whose text is blank - which models routinely emit next to tool
					// use. Drop those blocks before they poison the rest of the conversation.
					// (Content is null rather than empty when the service returns no blocks at all.)
					responseMessage.Content = [.. (responseMessage.Content ?? [])
						.Where(c => c.Text == null || !string.IsNullOrWhiteSpace(c.Text))];

					if (responseMessage.Content.Count > 0)
					{
						messages.Add(responseMessage);
					}

					var text = responseMessage.Text();
					var reason = responseMessage.Reason().Serialize();
					context.Reason = reason?.Text;
					if (!string.IsNullOrWhiteSpace(text))
					{
						logger.LogDebug("{Role}: {Text}", responseMessage.Role, text);
						var eventTask = events?.AfterMessage?.Invoke(responseMessage.Role, text ?? string.Empty);
						if (eventTask != null) await eventTask.ConfigureAwait(false);
						context.Text = text;
					}

					var generationData = new Message.GenerationData { GeneratedAt = DateTimeOffset.UtcNow, Duration = converseDurationStopwatch.Elapsed, InputTokens = tokenUsage.InputTokens, OutputTokens = tokenUsage.OutputTokens };

					if (stopReason == StopReason.Tool_use)
					{
						var toolUses = responseMessage.ToolsUse()
							.Select(toolUse => new ToolUsing.ToolUse
							{
								ToolUseId = toolUse.ToolUseId,
								ToolName = toolUse.Name,
								ToolInput = toolUse.Input.FromAmazonJson(),
								ToolResult = null,
							})
							.ToList();

						if (toolUses.Count == 0)
						{
							// Stopping for tool use without a tool block (a truncated stream can do
							// this) would otherwise re-send an identical request forever.
							logger.LogWarning("Stopped for tool use but no tool call was returned.");
							keepConversing = false;
						}

						resultMessages.Add(new Message(responseMessage.Role, text, reason,
							toolCalls: [.. toolUses.Select(t => new Message.ToolCall { Name = t.ToolName, Id = t.ToolUseId, Input = t.ToolInput })],
							toolResults: null,
							generationData: generationData));

						var toolResults = new List<ToolResultBlock>();
						foreach (var toolUse in toolUses)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var (toolResult, requiresApproval) = await ToolUsing.Use(tools, toolUse, responseMessage.Role, context, events, logger, cancellationToken: cancellationToken).ConfigureAwait(false);

							if (toolResult == null && requiresApproval != null)
							{
								return new AgentResult
								{
									Agent = this,
									Task = task,
									Tools = tools,
									Messages = resultMessages,
									PendingToolUses = new PendingToolUsesContext
									{
										Role = responseMessage.Role,
										Text = text,
										Reason = reason,
										Uses = toolUses,
										GenerationData = generationData,
									},
								};
							}
							else if (toolResult != null)
							{
								toolUse.ToolResult = JsonSerializer.Serialize(toolResult.Result);

								var toolResultMessage = GetAsToolResultMessage(toolUse.ToolUseId, toolResult.Result);
								toolResults.Add(toolResultMessage);

								if (context.Cancelled)
								{
									keepConversing = false;
									break;
								}
							}
							else throw new ArgumentException("No tool result and no approval requirement.");
						}

						// No results means a contentless message, which Bedrock rejects.
						if (toolResults.Count > 0 && (!context.Cancelled || context.RememberToolResultWhenCancelled))
						{
							messages.Add(ConversationRole.User.Says(toolResults));
							resultMessages.Add(new Message(ConversationRole.User, "", toolResults: [.. toolResults.Select(t => new Message.ToolResult { Id = t.ToolUseId, Output = t.Content.FirstOrDefault().Json.FromAmazonJson() })]));
						}
					}
					else
					{
						keepConversing = false;
						resultMessages.Add(new Message(responseMessage.Role, text, reason, generationData: generationData));
					}
				}
			}

			return new AgentResult { Agent = this, Task = task, Tools = tools, Messages = resultMessages };
		}

		public static Amazon.BedrockRuntime.Model.Tool CreateTool(Tool tool)
		{
			var definition = ToolUsing.GetToolDefinition(tool);
			return new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = definition.Name,
					Description = definition.Description,
					InputSchema = new ToolInputSchema
					{
						Json = definition.Schema.ToAmazonJson(),
					},
				}
			};
		}

		private static ToolResultBlock GetAsToolResultMessage(string toolUseId, Amazon.Runtime.Documents.Document result)
		{
			return new ToolResultBlock
			{
				ToolUseId = toolUseId,
				Content =
				[
					new ToolResultContentBlock
					{
						Json = result
					}
				]
			};
		}

		public static ToolResultBlock GetAsToolResultMessage(string toolUseId, object? result)
		{
			return GetAsToolResultMessage(toolUseId, result switch
			{
				JsonElement { ValueKind: JsonValueKind.Array } j => j.ToString().ToAmazonJson(),
				JsonElement { ValueKind: JsonValueKind.Object } j => j.ToString().ToAmazonJson(),
				_ => Amazon.Runtime.Documents.Document.FromObject(result switch
				{
					null => new { },
					bool b => new { result = b },
					int i => new { result = i },
					long l => new { result = l },
					double d => new { result = d },
					string s => new { result = s },
					Array a => new { result = a },
					IList c => new { result = c },
					JsonElement { ValueKind: JsonValueKind.Null } j => new { },
					JsonElement { ValueKind: JsonValueKind.True } j => new { result = j.GetBoolean() },
					JsonElement { ValueKind: JsonValueKind.False } j => new { result = j.GetBoolean() },
					JsonElement { ValueKind: JsonValueKind.Number } j => new { result = j.GetDouble() },
					JsonElement { ValueKind: JsonValueKind.String } j => new { result = j.GetString() },
					_ => result,
				}),
			});
		}
	}
}
