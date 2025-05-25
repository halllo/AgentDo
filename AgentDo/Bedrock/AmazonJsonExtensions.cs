using Amazon.Runtime.Documents;
using Amazon.Runtime.Documents.Internal.Transform;
using Amazon.Runtime.Internal.Transform;
using Amazon.Runtime.Internal.Util;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDo.Bedrock
{
	public static class AmazonJsonExtensions
	{
		public static Document ToAmazonJson(this JsonNode json) => json.ToJsonString(JsonSchemaExtensions.OutputOptions).ToAmazonJson();
		public static Document ToAmazonJson(this JsonDocument json) => json.RootElement.GetRawText().ToAmazonJson();
		public static Document ToAmazonJson(this string json)
		{
			using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
			using var context = new JsonUnmarshallerContext(stream, false, null);
			var reader = new StreamingUtf8JsonReader(stream);
			var unmarshaller = DocumentUnmarshaller.Instance;
			var unmarshalled = unmarshaller.Unmarshall(context, ref reader);
			return unmarshalled;

			//return Document.FromObject(JsonMapper.ToObject(json)); //cannot deserialize "default":null but throws NRE
		}

		public static string FromAmazonJson(this Document amazonJson)
		{
			if (amazonJson.IsDictionary() || amazonJson.IsList())
			{
				using var stream = new MemoryStream();
				var jsonWriter = new Utf8JsonWriter(stream);
				DocumentMarshaller.Instance.Write(jsonWriter, amazonJson);
				jsonWriter.Flush();
				var marshalled = Encoding.UTF8.GetString(stream.ToArray());
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
				? JsonSchemaExtensions.DeserializationOptions.WithConverters(JsonSchemaExtensions.GetAutoDiscoveredConverters(typeof(T)))
				: JsonSchemaExtensions.DeserializationOptions;

			var t = JsonSerializer.Deserialize<T>(json, deserializationOptions);
			return t;
		}
	}
}
