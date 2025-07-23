using AgentDo.Content;

namespace AgentDo
{
	public interface IAgent
	{
		Task<AgentResult> Do(Prompt task, List<Tool> tools, Events? events = null, CancellationToken cancellationToken = default);
	}
}
