namespace AgentDo.Content
{
	public class Prompt
	{
		//taken from https://docs.anthropic.com/en/docs/build-with-claude/tool-use#chain-of-thought-tool-use
		public readonly static string ClaudeChainOfThought = @"Answer the user's request using relevant tools (if they are available). 
Before calling a tool, do some analysis within <thinking></thinking> tags. 
First, think about which of the provided tools is the relevant tool to answer the user's request. 
Second, go through each of the required parameters of the relevant tool and determine if the user has directly provided or given enough information to infer a value. 
When deciding if the parameter can be inferred, carefully consider all the context including the return values from other tools to see if it supports optaining a specific value.
If all of the required parameters are present or can be reasonably inferred, close the thinking tag and proceed with the tool call.
BUT, if one of the values for a required parameter is missing, DO NOT invoke the function (not even with fillers for the missing params) and instead ask the user to provide the missing parameters. 
DO NOT ask for more information on optional parameters if it is not provided.
----
";

		public string Text { get; }
		public List<Image> Images { get; }
		public List<Document> Documents { get; }
		public AgentResult? AgentContext { get; }

		public Prompt(string text) : this(text, [], [], null)
		{
		}

		public Prompt(string text, params IEnumerable<Image> images) : this(text, images, [], null)
		{
		}

		public Prompt(string text, params IEnumerable<Document> documents) : this(text, [], documents, null)
		{
		}

		public Prompt(string text, AgentResult? agentContext) : this(text, [], [], agentContext)
		{
		}

		public Prompt(string text, IEnumerable<Image> images, IEnumerable<Document> documents, AgentResult? agentContext)
		{
			Text = text;
			Images = [.. images];
			Documents = [.. documents];
			AgentContext = agentContext;
		}

		public static implicit operator Prompt(string text) => new(text);
	}
}
