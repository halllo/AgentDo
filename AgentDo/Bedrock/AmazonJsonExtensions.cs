using Amazon.Runtime.Documents;
using Amazon.Runtime.Documents.Internal.Transform;
using Amazon.Runtime.Internal.Transform;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ThirdParty.Json.LitJson;

namespace AgentDo.Bedrock
{
	public static class AmazonJsonExtensions
	{
		public static Document ToAmazonJson(this JsonNode json) => json.ToJsonString(JsonSchemaExtensions.OutputOptions).ToAmazonJson();
		public static Document ToAmazonJson(this string json)
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			using var context = new JsonUnmarshallerContext(stream, false, null);
			var unmarshaller = DocumentUnmarshaller.Instance;
			var unmarshalled = unmarshaller.Unmarshall(context);
			return unmarshalled;
		}

		public static string FromAmazonJson(this Document amazonJson)
		{
			if (amazonJson.IsDictionary() || amazonJson.IsList())
			{
				var sb = new StringBuilder();
				var jsonWriter = new JsonWriter(sb);
				DocumentMarshaller.Instance.Write(jsonWriter, amazonJson);
				var marshalled = sb.ToString();
				return marshalled;
			}
			else
			{
				throw new NotSupportedException("Only dictionaries and lists can be marshalled.");
			}
		}

		public static T? FromAmazonJson<T>(this Document amazonJson, bool autoDiscoverConverters = false)
		{
			var json = amazonJson.FromAmazonJson();

			var deserializationOptions = autoDiscoverConverters
				? JsonSchemaExtensions.DeserializationOptions.WithAutoDiscoveredConverters(typeof(T))
				: JsonSchemaExtensions.DeserializationOptions;

			var t = JsonSerializer.Deserialize<T>(json, deserializationOptions);
			return t;
		}
	}
}
