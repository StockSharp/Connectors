namespace StockSharp.Zerodha.Native;

internal static class ZerodhaFormEncoder
{
	public static List<KeyValuePair<string, string>> Encode<T>(T request)
		where T : class
	{
		var values = new List<KeyValuePair<string, string>>();
		if (request == null)
			return values;

		foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			var field = property.GetCustomAttribute<KiteFormFieldAttribute>();
			if (field == null)
				continue;
			var value = property.GetValue(request);
			if (value == null || value is string text && text.IsEmpty())
				continue;
			values.Add(new(field.Name, Format(value)));
		}
		return values;
	}

	public static string ToQueryString<T>(T request)
		where T : class
		=> string.Join("&", Encode(request)
			.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

	private static string Format(object value)
		=> value switch
		{
			bool flag => flag ? "1" : "0",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};
}
