using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace AgentDo.OpenAI.Like
{
	public class OpenAILikeClient
	{
		public class Options
		{
			public string? Model { get; set; }
			public double? Temperature { get; set; }
		}

		public record Message(string Role, string Content, string? ToolCallId = null);
		public record Tool(string Name, string Description, JsonDocument Schema);

		private record CompletionResponseRaw(CompletionResponse[] Choices);
		public record CompletionResponse(string FinishReason, GeneratedMessage Message);
		public record GeneratedMessage(string Role, string? Content = null, ToolCall[]? ToolCalls = null);
		public record ToolCall(string Id, string Type, FunctionCall Function);
		public record FunctionCall(string Name, string Arguments);

		private readonly HttpClient http;
		private readonly IOptions<Options> options;
		private static readonly JsonSerializerOptions responseDeserializationOptions = new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		};

		public OpenAILikeClient(HttpClient http, IOptions<Options> options)
		{
			this.http = http;
			this.options = options;
		}

		public async Task<CompletionResponse> ChatCompletion(IEnumerable<Message> messages, IEnumerable<Tool> tools)
		{
			var content = new
			{
				model = options.Value.Model,
				messages = messages.Select(m => new
				{
					role = m.Role,
					content = m.Content,
					tool_call_id = m.ToolCallId,
				}).ToArray(),
				tools = tools.Select(t => new
				{
					type = "function",
					function = new
					{
						name = t.Name,
						description = t.Description,
						parameters = t.Schema,
					},
				}).ToArray(),
				temperature = options.Value.Temperature ?? 0.0,
				//max_tokens = -1,
				//presence_penalty = -1,
				stream = false,
			};

			var jsonContent = JsonSerializer.Serialize(content);
			var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

			var response = await http.PostAsync("v1/chat/completions", httpContent);
			response.EnsureSuccessStatusCode();

			var responseBody = await response.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<CompletionResponseRaw>(responseBody, responseDeserializationOptions)!.Choices.Single();
		}
	}
}
