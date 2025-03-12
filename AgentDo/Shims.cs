namespace System.Runtime.CompilerServices
{
	// Support for netstandard2.0 taken from https://stackoverflow.com/a/70034587/6466378.
	[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
	internal sealed class CallerArgumentExpressionAttribute : Attribute
	{
		public CallerArgumentExpressionAttribute(string parameterName)
		{
			ParameterName = parameterName;
		}

		public string ParameterName { get; }
	}
}

namespace System.Runtime.CompilerServices
{
	// Support for netstandard2.0 taken from https://stackoverflow.com/a/64749403/6466378.
	internal static class IsExternalInit { }
}