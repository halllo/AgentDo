using AgentDo.Content;
using System.Text.Json.Nodes;

namespace AgentDo
{
	public class AgentContext
	{
		public List<Message> Messages { get; set; } = null!;

		public ApprovalRequest? PendingApproval { get; set; } = null!;

		public void Deconstruct(out List<Message> messages, out ApprovalRequest? pendingApproval)
		{
			messages = Messages;
			pendingApproval = PendingApproval;
		}
	}

	public class ApprovalRequest
	{
		internal IAgent? Agent { get; set; }
		internal Prompt? Prompt { get; set; }
		internal List<Tool>? Tools { get; set; }
		internal AgentContext? AgentContext { get; set; }

		public string ToolName { get; set; } = null!;
		public string? ToolId { get; set; }
		public JsonObject? Inputs { get; set; }

		internal ApprovalRequest(ToolUsing.ApprovalRequired approvalRequired, IAgent agent, Prompt prompt, List<Tool> tools, AgentContext agentContext)
		{
			this.ToolName = approvalRequired.ToolName;
			this.ToolId = approvalRequired.ToolId;
			this.Inputs = approvalRequired.Inputs;
			this.Agent = agent;
			this.Prompt = prompt;
			this.Tools = tools;
			this.AgentContext = agentContext;
		}

		public Task<AgentContext> ApproveAndContinue(CancellationToken cancellationToken = default)
		{
			if (Agent == null || Prompt == null || Tools == null || AgentContext == null) 
				throw new InvalidOperationException("Approval request was not created by an agent, so it cannot be approved and continued.");
			
			return Agent.Do(new Prompt(Prompt.Text, AgentContext), Tools, cancellationToken);
		}
	}
}
