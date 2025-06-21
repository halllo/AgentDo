using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AgentDo.AgentResult;

namespace AgentDo
{
	public class ToolUsing
	{
		public static JsonObject GetInputsAsSchema(MethodInfo method)
		{
			var methodParameters = method.GetParameters().Where(p => p.ParameterType != typeof(Tool.Context));
			var toolPropertiesDictionary = methodParameters.ToDictionary(p => p.Name ?? string.Empty, p => new
			{
				Type = p.ParameterType,
				p.GetCustomAttribute<DescriptionAttribute>()?.Description,
				Required = p.GetCustomAttribute<RequiredAttribute>() != null || !IsNullable(p),
			});

			var schema = new JsonObject
			{
				["type"] = "object",
				["properties"] = new JsonObject(toolPropertiesDictionary.Select(p => new KeyValuePair<string, JsonNode?>(
					key: p.Key,
					value: p.Value.Type.ToJsonSchema(p.Value.Description)))),
				["required"] = new JsonArray(toolPropertiesDictionary.Where(kvp => kvp.Value.Required).Select(kvp => (JsonNode)kvp.Key).ToArray()),
			};
			return schema;

			///copilot generated, not sure if it's 100% correct
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

		public static async Task<object?> Use(Delegate tool, JsonObject inputs, Tool.Context context, Action<object?[]>? beforeInvoke = null, CancellationToken cancellationToken = default)
		{
			var method = tool.GetMethodInfo();

			var autoDiscoverConverters = new AutoDiscoverConverters();
			var parameters = method.GetParameters()
				.Select(p =>
					p.ParameterType == typeof(Tool.Context) ? context :
					p.ParameterType == typeof(JsonObject) ? inputs :
					p.ParameterType == typeof(JsonDocument) ? JsonDocument.Parse(inputs.ToJsonString(JsonSchemaExtensions.OutputOptions)) :
					inputs.TryGetPropertyValue(p.Name, out var value) ? value.As(p.ParameterType, autoDiscoverConverters) : default
				)
				.ToArray();

			beforeInvoke?.Invoke(parameters);

			var returnValue = tool.DynamicInvoke(parameters);
			object? result;
			if (returnValue is Task task)
			{
				await task;
				var taskResult = task.GetType().GetProperty("Result").GetValue(task);
				result = taskResult == null || taskResult.GetType().Name == "VoidTaskResult" ? null : taskResult;
			}
			else
			{
				result = returnValue;
			}

			return result;
		}

		public record ToolResult(object? Result);
		public record ApprovalRequired();
	}

	public abstract class ToolUsing<TTool> : ToolUsing
	{
		public TTool GetToolDefinition(Tool tool)
		{
			var method = tool.Delegate.GetMethodInfo();
			var methodDescription = method.GetCustomAttributes<DescriptionAttribute>().SingleOrDefault()?.Description ?? tool.Name;

			if (tool.Schema != null)
			{
				return CreateTool(tool.Name, methodDescription, tool.Schema);
			}
			else
			{
				JsonObject schema = GetInputsAsSchema(method);
				return CreateTool(tool.Name, methodDescription, JsonDocument.Parse(schema.ToJsonString(JsonSchemaExtensions.OutputOptions)));
			}
		}

		protected abstract TTool CreateTool(string name, string description, JsonDocument schema);

		internal async Task<(ToolResult?, ApprovalRequired?)> Use(IEnumerable<Tool> tools, PendingToolUse toolUse, string role, Tool.Context context, ILogger? logger, bool ignoreInvalidSchema = false, bool ignoreUnknownTools = false, CancellationToken cancellationToken = default)
		{
			var requestedToolName = toolUse.ToolName;
			var toolToUse = tools.Where(tool => tool.Name == requestedToolName).SingleOrDefault();
			if (toolToUse == null)
			{
				logger?.LogError("{Role}: Tool {ToolName} not found.", role, requestedToolName);

				if (ignoreUnknownTools)
				{
					return (new ToolResult("Unknown tool. Dont call again!"), null);
				}
				else
				{
					throw new NotSupportedException(requestedToolName);
				}
			}
			else
			{
				return await Use(toolToUse, toolUse, role, context, logger, ignoreInvalidSchema: ignoreInvalidSchema, cancellationToken: cancellationToken);
			}
		}

		public async Task<(ToolResult?, ApprovalRequired?)> Use(Tool tool, PendingToolUse toolUse, string role, Tool.Context context, ILogger? logger, bool ignoreInvalidSchema = false, CancellationToken cancellationToken = default)
		{
			var name = toolUse.ToolName;
			var id = toolUse.ToolUseId;
			var inputs = toolUse.ToolInput;
			var logItsAndOutputs = tool.LogInputsAndOutputs;

			if (tool.RequireApproval && !toolUse.Approved)
			{
				logger?.LogInformation("{Tool}: Tool {ToolUse} requires approval to invoke.", id, name);
				return (null, new ApprovalRequired());
			}

			try
			{
				object? result = await Use(tool.Delegate, inputs, context, beforeInvoke: parameters =>
				{
					if (logItsAndOutputs)
					{
						logger?.LogInformation("{Role}: Invoking {ToolUse}({Parameters})...", role, name, JsonSerializer.Serialize(parameters));
					}
					else
					{
						logger?.LogInformation("{Role}: Invoking {ToolUse}()...", role, name);
					}
				}, cancellationToken);

				if (logItsAndOutputs)
				{
					logger?.LogInformation("{Tool}: {Result}" + (context?.Cancelled ?? false ? " Cancelled!" : string.Empty), id, JsonSerializer.Serialize(result));
				}
				else
				{
					logger?.LogInformation("{Tool}:" + (context?.Cancelled ?? false ? " Cancelled!" : string.Empty), id);
				}
				return (new ToolResult(result), null);
			}
			catch (JsonException invalidSchema) when (ignoreInvalidSchema)
			{
				logger?.LogError(invalidSchema, "{Role}: Invoking {ToolUse}(@Input) failed because invalid schema.", role, name, inputs);
				return (new ToolResult("failed"), null);
			}
		}
	}
}
