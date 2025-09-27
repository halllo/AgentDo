using AgentDo.Content;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace AgentDo
{
	public class AgentResult
	{
		[JsonIgnore]
		public IAgent Agent { get; set; } = null!;
		[JsonIgnore]
		public Prompt Task { get; set; } = null!;
		[JsonIgnore]
		public List<Tool> Tools { get; set; } = null!;

		public List<Message> Messages { get; set; } = null!;

		public PendingToolUsesContext? PendingToolUses { get; set; }

		public class PendingToolUsesContext
		{
			public string Role { get; set; } = null!;
			public string Text { get; set; } = null!;
			public List<ToolUsing.ToolUse> Uses { get; set; } = null!;
			public Message.GenerationData GenerationData { get; set; } = null!;
		}

		public ToolUsing.ToolUse? Approvable => PendingToolUses?.Uses.SkipWhile(u => u.ToolResult != null).FirstOrDefault();
		public bool NeedsApprovalToContinue => Approvable != null;
		public async Task<AgentResult> ApproveAndContinue(ILogger? logger = null)
		{
			var use = PendingToolUses?.Uses.SkipWhile(u => u.ToolResult != null).FirstOrDefault();
			if (use == null)
			{
				throw new InvalidOperationException("No pending tool uses to approve.");
			}

			logger?.LogInformation("Approving tool use: {ToolName}", use.ToolName);
			use.Approved = true;

			var continueTask = new Prompt(Task.Text, Task.Images, Task.Documents, this);
			var newResult = await Agent.Do(continueTask, Tools);
			return newResult;
		}

		public AgentResult KeepOnly(int maxMessages)
		{
			if (Messages.Count <= maxMessages)
			{
				return this;
			}

			int skip = Messages.Count - maxMessages;
			var reduced = new AgentResult
			{
				Agent = Agent,
				Task = Task,
				Tools = Tools,
				PendingToolUses = PendingToolUses,
				Messages = Messages
					.Where((m, index) => 
						index >= skip //skip the first messages (false)...
						|| 
						(string.Equals(m.Role, "system", StringComparison.InvariantCultureIgnoreCase) //...unless its a system message...
							? (skip == skip++) //...then take it (evaluates to true) and at the same time prepare to skip one more non-system message.
							: false)
						)
					.SkipWhile(m => m.ToolResults != null)
					.ToList(),
			};
			return reduced;
		}
	}
}
