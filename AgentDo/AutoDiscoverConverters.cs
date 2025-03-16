using System.Reflection;
using System.Text.Json.Serialization;

namespace AgentDo
{
	internal class AutoDiscoverConverters
	{
		private HashSet<Type> alreadyLookedAtTypes;
		private HashSet<ConvertFromStringAttribute> discoveredConverters;
		private Dictionary<ConvertFromStringAttribute, JsonConverter> converterInstances;

		public AutoDiscoverConverters()
		{
			alreadyLookedAtTypes = [];
			discoveredConverters = [];
			converterInstances = [];
		}

		public void CollectRecursivelyFrom(Type type) => CollectRecursivelyFrom(type, type);
		private void CollectRecursivelyFrom(Type rootType, Type type)
		{
			alreadyLookedAtTypes.Add(type);
			foreach (var propertyType in type.GetProperties()
				.Select(p => p.PropertyType.IsArray ? p.PropertyType.GetElementType() : p.PropertyType)
				.Distinct()
				.Where(t => t.Assembly == rootType.Assembly || t.Assembly == typeof(ConvertFromStringAttribute).Assembly)
				.Where(t => !alreadyLookedAtTypes.Contains(t)))// when whe have already looked at the type, we don't need to look at it again
			{
				var selfConverting = propertyType.GetCustomAttribute<ConvertFromStringAttribute>(inherit: true);
				if (selfConverting != null)
				{
					discoveredConverters.Add(selfConverting);
				}

				CollectRecursivelyFrom(rootType, propertyType);
			}
		}

		public IEnumerable<JsonConverter> GetConverters()
		{
			foreach (var converter in discoveredConverters)
			{
				if (!converterInstances.TryGetValue(converter, out var jsonConverter))
				{
					jsonConverter = (JsonConverter)Activator.CreateInstance(converter.ConverterType);
					converterInstances.Add(converter, jsonConverter);
				}
				yield return jsonConverter;
			}
		}
	}
}
