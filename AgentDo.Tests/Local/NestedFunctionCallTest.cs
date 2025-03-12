using System.Text.Json;

namespace AgentDo.Tests.Local
{
    [TestClass]
    public sealed class NestedFunctionCallTest
    {
		record Person(string Name, int Age);

		[TestMethodWithDI]
        public void NestedFunctionCall()
		{
			var nestedFunctionCall = "{\"person\":{\"name\":\"Manuel Naujoks\",\"age\":{\"function_name\":\"calculate_age\",\"args\":[\"1986-09-07T00:00:00\",{\"function_name\":\"get_today\",\"args\":[]}]}}}";
			var person = JsonSerializer.Deserialize<Person>(nestedFunctionCall);

			//todo: how can I have more control over the deserialization process?
		}
	}
}
