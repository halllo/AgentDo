using System.Text.Json;
using static AgentDo.Cli.Verbs.DoTask;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class DecimalParserTest
	{
		[DataTestMethod]
		[DataRow("1,99", 1.99)]
		[DataRow("1.99", 1.99)]
		[DataRow("-1,99", -1.99)]
		[DataRow("-1.99", -1.99)]
		[DataRow("1000,99", 1000.99)]
		[DataRow("1000.99", 1000.99)]
		[DataRow("-1000,99", -1000.99)]
		[DataRow("-1000.99", -1000.99)]
		[DataRow("1.000,99", 1000.99)]
		[DataRow("1,000.99", 1000.99)]
		[DataRow("-1.000,99", -1000.99)]
		[DataRow("-1,000.99", -1000.99)]
		[DataRow("1.000.000,99", 1000000.99)]
		[DataRow("1,000,000.99", 1000000.99)]
		public void StringToDecimal(string input, double expected)
		{
			Assert.AreEqual((decimal)expected, Convert(input));
		}

		private static decimal Convert(string input)
		{
			var serialized = JsonSerializer.Serialize(new { Amount = input });
			var deserialized = JsonSerializer.Deserialize<ObjectWithAmount>(serialized, new JsonSerializerOptions
			{
				Converters = { new GermanOrEnglishAmounts() }
			})!;
			return deserialized.Amount.Value;
		}

		record ObjectWithAmount(Amount Amount);
	}
}
