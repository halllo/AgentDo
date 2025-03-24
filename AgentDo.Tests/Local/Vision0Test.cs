﻿using AgentDo.Content;
using AgentDo.OpenAI.Like;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo.Tests.Local
{
	[TestClass]
	public sealed class Vision0Test
	{
		record Invoice(
			string Number,
			DateTime Date,
			Amount Total,
			string Currency,
			string? Iban = null,
			string? Bic = null);

		[TestMethodWithDI]
		public async Task LocalWithImageAndSelfConvertingSchema([FromKeyedServices("local")] OpenAILikeClient client)
		{
			using var image = Image.From(new FileInfo(@"C:\Users\manue\Downloads\Inbox\Rechnung_2241198869.pdf.0.png"));
			
			OpenAILikeClient.Message[] messages =
			[
				new ("system", @"Answer the user's request using relevant tools. Use only the tools provided. If parameters for the tools are missing and cannot be inferred, dont call the tool."),
				new ("user", ContentArray: [
					new ("text", "Here is my document."),
					new ("image_url", PngImage: image.Stream),
				])
			];

			OpenAILikeClient.Tool[] tools =
			[
				new ("Invoice", "Understand the invoice.", JsonDocument.Parse(typeof(Invoice).ToJsonSchemaString()))
			];

			var completion = await client.ChatCompletion(messages, tools);
			Assert.IsNotNull(completion.Message.ToolCalls);

			var toolCall = completion.Message.ToolCalls[0].Function;
			using JsonDocument functionArguments = JsonDocument.Parse(toolCall.Arguments);

			Console.WriteLine(JsonSerializer.Serialize(functionArguments));
			var invoice = functionArguments.As<Invoice>(autoDiscoverConverters: true)!;

			Assert.AreEqual(expected: 23.13m, actual: invoice.Total.Value, delta: 0.01m);
		}

		class GermanAmounts : JsonConverter<Amount>
		{
			public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				if (reader.TokenType == JsonTokenType.String)
				{
					return new Amount(decimal.Parse(reader.GetString()!, NumberStyles.Any, CultureInfo.GetCultureInfo("de")));
				}
				else if (reader.TokenType == JsonTokenType.Number)
				{
					return new Amount(reader.GetDecimal());
				}
				throw new JsonException();
			}

			public override void Write(Utf8JsonWriter writer, Amount value, JsonSerializerOptions options) => throw new NotImplementedException();
		}
	}
}
