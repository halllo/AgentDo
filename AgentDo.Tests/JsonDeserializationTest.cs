using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class JsonDeserializationTest
	{
		record Invoice(
			string Number,
			DateTime Date,
			decimal Total,
			string Currency);

		[TestMethod]
		public void GermanDateAndNumberFormat()
		{
			var invoice = JsonSerializer.Deserialize<Invoice>("{\"number\":\"224119869\",\"date\":\"20.08.2024\",\"total\":\"23,13\",\"currency\":\"EUR\"}", new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				Converters = { new DateTimeJsonConverter(), new DecimalJsonConverter() },
			})!;

			Assert.AreEqual(new DateTime(2024, 8, 20), invoice.Date);
			Assert.AreEqual(23.13m, invoice.Total);
		}

		public class DateTimeJsonConverter : JsonConverter<DateTime>
		{
			public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
				=> DateTime.ParseExact(reader.GetString()!, "dd.MM.yyyy", new CultureInfo("de"));

			public override void Write(Utf8JsonWriter writer, DateTime dateTimeValue, JsonSerializerOptions options)
				=> writer.WriteStringValue(dateTimeValue.ToString("dd.MM.yyyy", new CultureInfo("de")));
		}

		public class DecimalJsonConverter : JsonConverter<decimal>
		{
			public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
				=> decimal.Parse(reader.GetString()!, NumberStyles.Any, new CultureInfo("de"));

			public override void Write(Utf8JsonWriter writer, decimal d, JsonSerializerOptions options)
				=> writer.WriteStringValue(d.ToString(new CultureInfo("de")));
		}
	}
}
