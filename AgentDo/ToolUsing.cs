using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Nodes;

namespace AgentDo
{
	public abstract class ToolUsing<TTool, TToolUse, TToolResult>
	{
		public TTool GetToolDefinition(Tool tool)
		{
			var method = tool.Delegate.GetMethodInfo();
			var methodDescription = method.GetCustomAttributes<DescriptionAttribute>().SingleOrDefault()?.Description ?? tool.Name;
			var methodParameters = method.GetParameters().Where(p => p.ParameterType != typeof(Tool.Context));
			var toolPropertiesDictionary = methodParameters.ToDictionary(p => p.Name ?? string.Empty, p => new
			{
				Type = p.ParameterType,
				p.GetCustomAttribute<DescriptionAttribute>()?.Description,
				Required = p.GetCustomAttribute<RequiredAttribute>() != null || !IsNullable(p),
			});

			return CreateTool(tool.Name, methodDescription, new JsonObject
			{
				["type"] = "object",
				["properties"] = new JsonObject(toolPropertiesDictionary.Select(p => new KeyValuePair<string, JsonNode?>(
					key: p.Key,
					value: p.Value.Type.ToJsonSchema(p.Value.Description)))),
				["required"] = new JsonArray(toolPropertiesDictionary.Where(kvp => kvp.Value.Required).Select(kvp => (JsonNode)kvp.Key).ToArray()),
			});

			///copilot generated, not sure if it's correct
			static bool IsNullable(ParameterInfo parameter)
			{
				if (parameter.ParameterType.IsValueType)
				{
					return Nullable.GetUnderlyingType(parameter.ParameterType) != null;
				}

				var nullableAttribute = parameter.GetCustomAttributes()
					.FirstOrDefault(attr => attr.GetType().FullName == "System.Runtime.CompilerServices.NullableAttribute");

				if (nullableAttribute != null)
				{
					var field = nullableAttribute.GetType().GetField("NullableFlags");
					if (field != null)
					{
						var flags = (byte[])field.GetValue(nullableAttribute);
						return flags[0] == 2;
					}
				}

				return false;
			}
		}

		protected abstract TTool CreateTool(string name, string description, JsonObject schema);

		internal async Task<TToolResult> Use(IEnumerable<Tool> tools, TToolUse toolUse, object role, Tool.Context context, ILogger? logger)
		{
			var toolToUse = tools.Single(tool => tool.Name == GetToolName(toolUse).name);
			return await Use(toolToUse, toolUse, role, context, logger);
		}

		public async Task<TToolResult> Use(Tool tool, TToolUse toolUse, object role, Tool.Context context, ILogger? logger)
		{
			var (name, id) = GetToolName(toolUse);
			var inputs = GetToolInputs(toolUse);

			var autoDiscoverConverters = new AutoDiscoverConverters();

			var method = tool.Delegate.GetMethodInfo();
			var parameters = method.GetParameters()
				.Select(p => p.ParameterType == typeof(Tool.Context) ? context : 
					inputs.TryGetPropertyValue(p.Name, out var value) ? value.As(p.ParameterType, autoDiscoverConverters) : default
				)
				.ToArray();

			logger?.LogInformation("{Role}: Invoking {ToolUse}({@Parameters})...", role, name, parameters);

			var returnValue = tool.Delegate.DynamicInvoke(parameters);
			object? result;
			if (returnValue is Task task)
			{
				await task;
				var taskResult = task.GetType().GetProperty("Result").GetValue(task);
				result = taskResult.GetType().Name == "VoidTaskResult" ? null : taskResult;
			}
			else
			{
				result = returnValue;
			}

			logger?.LogInformation("{Tool}: {@Result}", id, result);
			return GetAsToolResult(toolUse, result);
		}

		protected abstract (string name, string id) GetToolName(TToolUse toolUse);
		protected abstract JsonObject GetToolInputs(TToolUse toolUse);
		protected abstract TToolResult GetAsToolResult(TToolUse toolUse, object? result);
	}
}
