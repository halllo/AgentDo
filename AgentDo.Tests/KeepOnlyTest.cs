namespace AgentDo.Tests
{
	[TestClass]
	public class KeepOnlyTest
	{
		[TestMethod]
		public void DropsFirstMessages()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "" },
					new Message { Role = "assistant", Text = "" },
				]
			};

			var reduced = agentResult.KeepOnly(1);

			Assert.AreEqual(1, reduced.Messages.Count);
			Assert.AreEqual("assistant", reduced.Messages[0].Role);
		}

		[TestMethod]
		public void DropsAllMessages()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "" },
					new Message { Role = "assistant", Text = "" },
				]
			};

			Assert.AreEqual(0, agentResult.KeepOnly(0).Messages.Count);
			Assert.AreEqual(0, agentResult.KeepOnly(-1).Messages.Count);
		}

		[TestMethod]
		public void KeepsAllMessages()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "" },
					new Message { Role = "assistant", Text = "" },
				]
			};

			Assert.AreEqual(2, agentResult.KeepOnly(2).Messages.Count);
			Assert.AreEqual(2, agentResult.KeepOnly(3).Messages.Count);
			Assert.AreEqual(2, agentResult.KeepOnly(4).Messages.Count);
		}

		[TestMethod]
		public void ForgetsToolCallAndResultTogether()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "user", Text = "" },
					new Message { Role = "assistant", ToolCalls = [ new Message.ToolCall { Id = "t1" } ] },
					new Message { Role = "tool", ToolResults = [ new Message.ToolResult { Id = "t1" } ] },
					new Message { Role = "assistant", Text = "" },
				]
			};

			Assert.AreEqual(4, agentResult.KeepOnly(4).Messages.Count);
			Assert.AreEqual(3, agentResult.KeepOnly(3).Messages.Count);
			Assert.AreEqual(1, agentResult.KeepOnly(2).Messages.Count);//forgets tool call and result
		}

		[TestMethod]
		public void KeepsSystemMessages()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "system", Text = "" },
					new Message { Role = "user", Text = "" },
					new Message { Role = "assistant", Text = "" },
				]
			};

			var reduced = agentResult.KeepOnly(2);
			Assert.AreEqual(2, reduced.Messages.Count);
			Assert.AreEqual("system", reduced.Messages[0].Role);
			Assert.AreEqual("assistant", reduced.Messages[1].Role);
		}

		[TestMethod]
		public void KeepsSystemsMessages()
		{
			var agentResult = new AgentResult
			{
				Messages =
				[
					new Message { Role = "system", Text = "" },
					new Message { Role = "user", Text = "" },
					new Message { Role = "system", Text = "" },
					new Message { Role = "assistant", Text = "" },
				]
			};

			Assert.AreEqual(4, agentResult.KeepOnly(5).Messages.Count);
			Assert.AreEqual(4, agentResult.KeepOnly(4).Messages.Count);
			{
				var reduced = agentResult.KeepOnly(3);
				Assert.AreEqual(3, reduced.Messages.Count);
				Assert.AreEqual("system", reduced.Messages[0].Role);
				Assert.AreEqual("system", reduced.Messages[1].Role);
				Assert.AreEqual("assistant", reduced.Messages[2].Role);
			}
			{
				var reduced = agentResult.KeepOnly(2);
				Assert.AreEqual(2, reduced.Messages.Count);
				Assert.AreEqual("system", reduced.Messages[0].Role);
				Assert.AreEqual("system", reduced.Messages[1].Role);
			}
			{
				var reduced = agentResult.KeepOnly(1);
				Assert.AreEqual(2, reduced.Messages.Count);
				Assert.AreEqual("system", reduced.Messages[0].Role);
				Assert.AreEqual("system", reduced.Messages[1].Role);
			}
		}
	}
}
