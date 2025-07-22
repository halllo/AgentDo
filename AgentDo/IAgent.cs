using AgentDo.Content;

namespace AgentDo
{
	public interface IAgent
	{
		Task<AgentResult> Do(Prompt task, List<Tool> tools, OnMessage? onMessage = null, CancellationToken cancellationToken = default);
	}

	public delegate void OnMessage(string role, string message);
}
