namespace AgentDo
{
	public interface IAgent
	{
		Task<List<Message>> Do(string task, List<Tool> tools);
	}
}
