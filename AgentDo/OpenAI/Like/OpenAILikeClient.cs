using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo.OpenAI.Like
{
	public class OpenAILikeClient
	{
		public class Options
		{
			public string? Model { get; set; }
			public double? Temperature { get; set; }
			public bool ParallelToolCalls { get; set; }
		}

		public record Message(string Role, string? Content = null, MessageContent[]? ContentArray = null, ToolCall[]? ToolCalls = null, string? ToolCallId = null);
		public record MessageContent(string Type, string? Text = null, MemoryStream? PngImage = null);
		public record Tool(string Name, string Description, JsonDocument Schema);

		private record CompletionResponseRaw(CompletionResponse[] Choices);
		public record CompletionResponse(string FinishReason, Message Message);
		public record ToolCall(string Id, string Type, FunctionCall Function);
		public record FunctionCall(string Name, string Arguments);

		private readonly HttpClient http;
		private readonly IOptions<Options> options;
		private static readonly JsonSerializerOptions snakeCaseLower = new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
				messages = messages.Select(m => m.ContentArray != null
					? (object) new
					{
						role = m.Role,
						content = m.ContentArray.Select(c => new
						{
							type = c.Type,
							text = c.Text,
							image_url = c.PngImage != null ? new { url = GetBase64EncodedUrlOfPng(c.PngImage) } : null,
						}),
						tool_calls = m.ToolCalls,
						tool_call_id = m.ToolCallId,
					}
					: (object) new
					{
						role = m.Role,
						content = m.Content,
						tool_calls = m.ToolCalls,
						tool_call_id = m.ToolCallId,
					}).ToArray(),
				parallel_tool_calls = options.Value.ParallelToolCalls,
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
				stream = false,
			};

			var jsonContent = JsonSerializer.Serialize(content, snakeCaseLower);
			var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

			var response = await http.PostAsync("v1/chat/completions", httpContent);
			response.EnsureSuccessStatusCode();

			var responseBody = await response.Content.ReadAsStringAsync();
			return JsonSerializer.Deserialize<CompletionResponseRaw>(responseBody, snakeCaseLower)!.Choices.Single();
		}

		private static string GetBase64EncodedUrlOfPng(MemoryStream pngStream) => $"data:image/png;base64,{Convert.ToBase64String(pngStream.ToArray())}";
	}
}
