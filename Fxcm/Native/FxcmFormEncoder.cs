namespace StockSharp.Fxcm.Native;

internal static class FxcmFormEncoder
{
	public static List<KeyValuePair<string, string>> Encode<T>(T request)
		where T : class
	{
		var values = new List<KeyValuePair<string, string>>();
		if (request == null)
			return values;

		foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			var field = property.GetCustomAttribute<FxcmFormFieldAttribute>();
			if (field == null)
				continue;

			var value = property.GetValue(request);
			if (value == null)
				continue;

			if (value is IEnumerable<string> strings)
			{
				foreach (var item in strings.Where(s => !s.IsEmpty()))
					values.Add(new(field.Name, item));
				continue;
			}

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
			bool flag => flag ? "true" : "false",
			DateTimeOffset date => date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			DateTime date => new DateTimeOffset(date.UtcKind()).ToUnixTimeSeconds()
				.ToString(CultureInfo.InvariantCulture),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};
}
