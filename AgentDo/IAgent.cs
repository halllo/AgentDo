namespace AgentDo
{
	public interface IAgent
	{
		Task<List<Message>> Do(Prompt task, List<Tool> tools);
	}
}
