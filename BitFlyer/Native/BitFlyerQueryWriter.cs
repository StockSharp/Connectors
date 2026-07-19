namespace StockSharp.BitFlyer.Native;

static class BitFlyerQueryWriter
{
	public static void Add(StringBuilder builder, ref bool hasValue,
		string name, string value)
	{
		if (value.IsEmpty())
			return;
		AppendPrefix(builder, ref hasValue);
		builder.Append(Uri.EscapeDataString(name));
		builder.Append('=');
		builder.Append(Uri.EscapeDataString(value));
	}

	public static void Add(StringBuilder builder, ref bool hasValue,
		string name, long? value)
	{
		if (value is null)
			return;
		Add(builder, ref hasValue, name,
			value.Value.ToString(CultureInfo.InvariantCulture));
	}

	public static void Add(StringBuilder builder, ref bool hasValue,
		string name, int? value)
	{
		if (value is null)
			return;
		Add(builder, ref hasValue, name,
			value.Value.ToString(CultureInfo.InvariantCulture));
	}

	private static void AppendPrefix(StringBuilder builder, ref bool hasValue)
	{
		if (hasValue)
			builder.Append('&');
		else
			hasValue = true;
	}
}
