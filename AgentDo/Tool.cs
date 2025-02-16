using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AgentDo
{
	public class Tool
	{
		public string Name { get; private set; }

		public Delegate Delegate { get; private set; }

		private Tool(string name, Delegate tool)
		{
			this.Name = name;
			this.Delegate = tool;
		}

		public static Tool From(Delegate tool, [CallerArgumentExpression("tool")] string toolName = "")
		{
			string actualToolName;
			if (toolName.Contains(' ') || toolName.Contains('.'))
			{
				var displayName = tool.GetMethodInfo().GetCustomAttribute<DescriptionAttribute>()?.Description;
				actualToolName = Regex.Replace(displayName!, @"[^a-zA-Z0-9]+(.)", m => m.Groups[1].Value.ToUpper()).Trim(' ').Trim('.');
			}
			else
			{
				actualToolName = toolName;
			}

			return new Tool(actualToolName, tool);
		}
	}
}
