using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDo
{
	/// <summary>
	/// Represents an amount of money. It can convert itself from a string and supports both German and English decimal and thousands separator.
	/// </summary>
	[ConvertFromString<StringToAmountConverter>]
	public record Amount(decimal Value);

	public class StringToAmountConverter : JsonConverter<Amount>
	{
		public override Amount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.String)
			{
				var numberString = reader.GetString()!;
				Console.Write("Converting " + numberString);

				// Remove any spaces
				numberString = numberString.Replace(" ", "");

				// Check if the string contains both comma and period
				if (numberString.Contains(",") && numberString.Contains("."))
				{
					// Determine which is the decimal separator by checking the last occurrence
					int lastComma = numberString.LastIndexOf(',');
					int lastPeriod = numberString.LastIndexOf('.');

					if (lastComma > lastPeriod)
					{
						// German format: 1.000.000,99
						numberString = numberString.Replace(".", "").Replace(",", ".");
					}
					else
					{
						// English format: 1,000,000.99
						numberString = numberString.Replace(",", "");
					}
				}
				else if (numberString.Contains(","))
				{
					// German format: 1.000.000,99 or 1.000,99 or 1,99
					numberString = numberString.Replace(".", "").Replace(",", ".");
				}
				else if (numberString.Contains("."))
				{
					// English format: 1,000,000.99 or 1,000.99 or 1.99
					numberString = numberString.Replace(",", "");
				}

				if (decimal.TryParse(numberString, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal converted))
				{

					Console.WriteLine(" into " + converted);
					return new Amount(converted);
				}

				throw new FormatException("The input string is not in a recognized format.");
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
