namespace StockSharp.CoinsPh.Native;

sealed class CoinsPhQueryBuilder
{
	private readonly StringBuilder _builder;

	public CoinsPhQueryBuilder()
	{
		_builder = new();
	}

	private CoinsPhQueryBuilder(string query)
	{
		_builder = new(query ?? string.Empty);
	}

	public CoinsPhQueryBuilder Clone()
		=> new(ToString());

	public CoinsPhQueryBuilder Add(string name, string value)
	{
		if (value.IsEmpty())
			return this;
		if (_builder.Length > 0)
			_builder.Append('&');
		_builder.Append(name.ThrowIfEmpty(nameof(name))).Append('=')
			.Append(Uri.EscapeDataString(value));
		return this;
	}

	public CoinsPhQueryBuilder Add(string name, int value)
		=> Add(name, value.ToString(CultureInfo.InvariantCulture));

	public CoinsPhQueryBuilder Add(string name, long value)
		=> Add(name, value.ToString(CultureInfo.InvariantCulture));

	public CoinsPhQueryBuilder Add(string name, decimal value)
		=> Add(name, value.ToString(CultureInfo.InvariantCulture));

	public CoinsPhQueryBuilder Add<TValue>(string name, TValue? value)
		where TValue : struct
		=> value is TValue actual
			? Add(name, Convert.ToString(actual, CultureInfo.InvariantCulture))
			: this;

	public CoinsPhQueryBuilder AddEnum<TValue>(string name, TValue? value)
		where TValue : struct, Enum
		=> value is TValue actual
			? Add(name, actual.GetWireValue())
			: this;

	public CoinsPhQueryBuilder AddEnum<TValue>(string name, TValue value)
		where TValue : struct, Enum
		=> Add(name, value.GetWireValue());

	public override string ToString()
		=> _builder.ToString();
}

static class CoinsPhEnumExtensions
{
	public static string GetWireValue<TValue>(this TValue value)
		where TValue : struct, Enum
	{
		var member = typeof(TValue).GetMember(value.ToString()).FirstOrDefault();
		return member?.GetCustomAttributes(typeof(EnumMemberAttribute), false)
			.Cast<EnumMemberAttribute>().FirstOrDefault()?.Value ??
			value.ToString().ToUpperInvariant();
	}
}
