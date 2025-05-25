using AgentDo.Content;

namespace AgentDo
{
	public interface IAgent
	{
		Task<AgentContext> Do(Prompt task, List<Tool> tools, CancellationToken cancellationToken = default);
	}
}
