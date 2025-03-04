using System.Text.Json.Serialization;

namespace AgentDo
{
	public class ConvertFromStringAttribute : Attribute
	{
		public Type ConverterType { get; }
		public ConvertFromStringAttribute(Type converterType)
		{
			ConverterType = converterType;
		}
	}

	public class ConvertFromStringAttribute<TConverter> : ConvertFromStringAttribute where TConverter : JsonConverter
	{
		public ConvertFromStringAttribute() : base(typeof(TConverter))
		{
		}
	}
}
