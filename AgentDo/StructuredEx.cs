using System.ComponentModel;

namespace AgentDo
{
	public static class StructuredEx
	{
		public static async Task<T> Get<T>(this IAgent agent, string task, CancellationToken cancellationToken = default)
		{
			bool assessed = false;
			T? t = default;
			var result = await agent.Do(
				task: task,
				tools: [
					Tool.From(toolName: "provide_assesment", tool: [Description("Call this tool to provide your assessment.")] (T assesment, Tool.Context context) =>
					{
						t = assesment;
						assessed = true;
						context.Suspend();
					}),
				],
				cancellationToken: cancellationToken);

			if (!assessed) throw new InvalidOperationException(result.Messages.Last().Text);
			else return t!;
		}

		public record Evaluation(bool Affirmative, string Explanation);
		public static Task<Evaluation> Eval(this IAgent agent, string task, CancellationToken cancellationToken = default) => agent.Get<Evaluation>(task, cancellationToken);
	}
}
