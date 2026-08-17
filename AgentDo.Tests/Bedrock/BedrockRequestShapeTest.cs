using AgentDo.Bedrock;
using AgentDo.Content;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentDo.Tests.Bedrock
{
	/// <summary>
	/// Bedrock enforces a handful of request-shape rules and answers with a 400 when they are
	/// broken: no blank text in a ContentBlock, no contentless message, no blank system prompt,
	/// and the conversation has to end with a user message. Breaking any of them used to surface
	/// as an intermittent failure in a live test, which is an expensive and unreliable way to
	/// find out. These drive the real BedrockAgent against a recording double instead - no
	/// network, no bill, no flakiness.
	/// </summary>
	[TestClass]
	public sealed class BedrockRequestShapeTest
	{
		/// <summary>
		/// Records every request and answers from a script. AmazonBedrockRuntimeClient declares
		/// ConverseAsync virtual and its credential-taking constructor does no I/O, so subclassing
		/// it is enough of a test double and nothing ever leaves the machine.
		/// </summary>
		private sealed class RecordingBedrock : AmazonBedrockRuntimeClient
		{
			public RecordingBedrock() : base("dummy-access-key-id", "dummy-secret-access-key", RegionEndpoint.EUCentral1) { }

			/// <summary>
			/// Snapshots, not the requests themselves: BedrockAgent hands the same live List to
			/// every ConverseRequest and keeps appending to it, so holding the request would show
			/// the conversation as it ended rather than as it was sent.
			/// </summary>
			public List<(List<Amazon.BedrockRuntime.Model.Message> Messages, List<SystemContentBlock>? System)> Requests { get; } = [];

			public Queue<(List<ContentBlock> Content, StopReason StopReason)> Replies { get; } = new();

			public void Replies_Add(StopReason stopReason, params ContentBlock[] content) => Replies.Enqueue(([.. content], stopReason));

			public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
			{
				Requests.Add(([.. request.Messages], request.System == null ? null : [.. request.System]));

				var (content, stopReason) = Replies.Count > 0
					? Replies.Dequeue()
					: ([new ContentBlock { Text = "done" }], StopReason.End_turn);

				return Task.FromResult(new ConverseResponse
				{
					Output = new ConverseOutput
					{
						Message = new Amazon.BedrockRuntime.Model.Message
						{
							Role = ConversationRole.Assistant,
							// A fresh list per call: the agent sanitizes this in place.
							Content = [.. content],
						}
					},
					StopReason = stopReason,
					Usage = new TokenUsage { InputTokens = 1, OutputTokens = 1 },
				});
			}
		}

		private static IAgent AgentOver(RecordingBedrock bedrock, string? systemPrompt = null) => new BedrockAgent(
			bedrock: bedrock,
			logger: NullLogger<BedrockAgent>.Instance,
			options: Options.Create(new BedrockAgentOptions
			{
				ModelId = "offline-model",
				Temperature = 0.0F,
				SystemPrompt = systemPrompt,
			}));

		/// <summary>Every rule Bedrock enforces on a request, stated once.</summary>
		private static void AssertBedrockWouldAcceptIt((List<Amazon.BedrockRuntime.Model.Message> Messages, List<SystemContentBlock>? System) request)
		{
			Assert.IsTrue(request.Messages.Count > 0, "Bedrock rejects a request with no messages at all.");

			for (int m = 0; m < request.Messages.Count; m++)
			{
				var message = request.Messages[m];
				Assert.IsTrue(message.Content != null && message.Content.Count > 0,
					$"messages.{m} carries no content; Bedrock rejects contentless messages.");

				for (int c = 0; c < message.Content!.Count; c++)
				{
					var text = message.Content[c].Text;
					Assert.IsFalse(text != null && string.IsNullOrWhiteSpace(text),
						$"The text field in the ContentBlock object at messages.{m}.content.{c} is blank.");
				}
			}

			Assert.AreEqual(ConversationRole.User.Value, request.Messages[request.Messages.Count - 1].Role.Value,
				"This model does not support assistant message prefill. The conversation must end with a user message.");

			foreach (var system in request.System ?? [])
			{
				Assert.IsFalse(system.Text != null && string.IsNullOrWhiteSpace(system.Text),
					"The text field in the SystemContentBlock is blank.");
			}
		}

		private static void AssertEveryRequestWouldBeAccepted(RecordingBedrock bedrock)
		{
			Assert.IsTrue(bedrock.Requests.Count > 0, "Expected at least one request.");
			foreach (var request in bedrock.Requests) AssertBedrockWouldAcceptIt(request);
		}

		[TestMethod]
		public async Task ResumingAConversationThatIsAlreadyFinishedDoesNotCallTheModel()
		{
			// The suspend/resume pattern is "Do(new Prompt(string.Empty, previousResult))". When
			// the previous run happened to finish on its own - no tool was suspended, nothing is
			// pending - there is nothing to resume and nothing to say, and the conversation ends
			// with the assistant. Sending that is a guaranteed 400.
			var bedrock = new RecordingBedrock();
			var finished = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "I would like to register Manuel Naujoks." },
					new Message { Role = "assistant", Text = "Manuel Naujoks is registered." },
				],
			};

			var resumed = await AgentOver(bedrock).Do(new Prompt(string.Empty, finished), []);

			Assert.IsEmpty(bedrock.Requests, "There was nothing to ask, so no call should have been made.");
			Assert.HasCount(2, resumed.Messages);
			Assert.AreEqual("Manuel Naujoks is registered.", resumed.Messages[^1].Text);
		}

		[TestMethod]
		public async Task ResumingRunsThePendingToolAndEndsOnAUserMessage()
		{
			// The other half of the same pattern: there *is* something to resume, so the tool runs
			// and its result becomes the user message the conversation has to end with.
			var bedrock = new RecordingBedrock();
			var suspended = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "I would like to register Manuel Naujoks." },
					new Message
					{
						Role = "assistant",
						Text = null,
						ToolCalls = [new Message.ToolCall { Id = "t1", Name = "getToday", Input = "{}" }],
					},
				],
			};

			await AgentOver(bedrock).Do(
				new Prompt(string.Empty, suspended),
				[Tool.From(() => "01 March 2025", toolName: "getToday")]);

			AssertEveryRequestWouldBeAccepted(bedrock);
			Assert.HasCount(1, bedrock.Requests);
		}

		[TestMethod]
		public async Task ModelReplyWithBlankTextIsNotEchoedBack()
		{
			// Models routinely emit a tool call with an empty text block in front of it. That reply
			// goes straight back out with the next request, so it has to be cleaned on the way in.
			var bedrock = new RecordingBedrock();
			bedrock.Replies_Add(StopReason.Tool_use,
				new ContentBlock { Text = "" },
				new ContentBlock { ToolUse = new ToolUseBlock { ToolUseId = "t1", Name = "getToday", Input = "{}".ToAmazonJson() } });
			bedrock.Replies_Add(StopReason.End_turn, new ContentBlock { Text = "Today is 01 March 2025." });

			await AgentOver(bedrock).Do("What day is it?", [Tool.From(() => "01 March 2025", toolName: "getToday")]);

			AssertEveryRequestWouldBeAccepted(bedrock);
			Assert.HasCount(2, bedrock.Requests);
		}

		[TestMethod]
		[DataRow("")]
		[DataRow("   ")]
		[DataRow("\r\n")]
		public async Task WhitespaceOnlyModelReplyIsNotEchoedBack(string blank)
		{
			var bedrock = new RecordingBedrock();
			bedrock.Replies_Add(StopReason.Tool_use,
				new ContentBlock { Text = blank },
				new ContentBlock { ToolUse = new ToolUseBlock { ToolUseId = "t1", Name = "getToday", Input = "{}".ToAmazonJson() } });
			bedrock.Replies_Add(StopReason.End_turn, new ContentBlock { Text = "Today is 01 March 2025." });

			await AgentOver(bedrock).Do("What day is it?", [Tool.From(() => "01 March 2025", toolName: "getToday")]);

			AssertEveryRequestWouldBeAccepted(bedrock);
		}

		[TestMethod]
		public async Task StoredMessageThatRehydratesIntoNothingIsDropped()
		{
			// A stored message can carry no text, no tool calls and no tool results at all.
			var bedrock = new RecordingBedrock();
			var previous = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "Whats the weather?" },
					new Message { Role = "assistant", Text = null },
				],
			};

			await AgentOver(bedrock).Do(new Prompt("And tomorrow?", previous), []);

			AssertEveryRequestWouldBeAccepted(bedrock);
			Assert.HasCount(2, bedrock.Requests[0].Messages, "The empty stored message should not have been sent.");
		}

		[TestMethod]
		public async Task ReplayedToolCallWithoutPreambleCarriesNoBlankText()
		{
			var bedrock = new RecordingBedrock();
			var previous = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "What day is it?" },
					// The model went straight to the tool without saying anything first.
					new Message
					{
						Role = "assistant",
						Text = "",
						ToolCalls = [new Message.ToolCall { Id = "t1", Name = "getToday", Input = "{}" }],
					},
					new Message
					{
						Role = "user",
						ToolResults = [new Message.ToolResult { Id = "t1", Output = "\"01 March 2025\"" }],
					},
				],
			};

			await AgentOver(bedrock).Do(
				new Prompt("And tomorrow?", previous),
				[Tool.From(() => "01 March 2025", toolName: "getToday")]);

			AssertEveryRequestWouldBeAccepted(bedrock);
		}

		[TestMethod]
		[DataRow("")]
		[DataRow("   ")]
		public async Task BlankSystemPromptIsNotSent(string blank)
		{
			// Binding SystemPrompt from a config key that exists but is empty yields "", which
			// Bedrock rejects on every single call.
			var bedrock = new RecordingBedrock();

			await AgentOver(bedrock, systemPrompt: blank).Do("Whats the weather?", []);

			AssertEveryRequestWouldBeAccepted(bedrock);
			Assert.IsTrue(bedrock.Requests[0].System == null || bedrock.Requests[0].System!.Count == 0);
		}

		[TestMethod]
		public async Task RealSystemPromptIsStillSent()
		{
			var bedrock = new RecordingBedrock();

			await AgentOver(bedrock, systemPrompt: "You are terse.").Do("Whats the weather?", []);

			Assert.AreEqual("You are terse.", bedrock.Requests[0].System![0].Text);
		}

		[TestMethod]
		public async Task StoppingForToolUseWithoutAToolCallTerminates()
		{
			// A truncated stream can report tool_use with no tool block. Re-sending the same
			// request forever would burn money on every iteration.
			var bedrock = new RecordingBedrock();
			bedrock.Replies_Add(StopReason.Tool_use, new ContentBlock { Text = "I will look that up." });

			var result = await AgentOver(bedrock).Do("What day is it?", [Tool.From(() => "01 March 2025", toolName: "getToday")]);

			Assert.HasCount(1, bedrock.Requests, "The agent should have stopped instead of re-asking.");
			Assert.IsNotNull(result);
		}
	}
}
