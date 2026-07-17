namespace StockSharp.LemonMarkets;

static class LemonMarketsExtensions
{
	public const string BoardCode = "LEMON";

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"stock" => SecurityTypes.Stock,
			"etf" => SecurityTypes.Etf,
			"fund" => SecurityTypes.Fund,
			_ => null,
		};

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("sell") ? Sides.Sell : Sides.Buy;

	public static OrderStates ToOrderState(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"created" or "confirmed" => OrderStates.Pending,
			"accepted" or "canceling" => OrderStates.Active,
			"executed" or "canceled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static SecurityId ToSecurityId(this LemonInstrument instrument)
	{
		var securityId = new SecurityId
		{
			SecurityCode = instrument.Isin,
			BoardCode = BoardCode,
		};
		if (!instrument.Isin.IsEmpty())
			securityId.Isin = instrument.Isin;
		return securityId;
	}

	public static SecurityId ToLemonSecurityId(this string isin)
	{
		var securityId = new SecurityId
		{
			SecurityCode = isin,
			BoardCode = BoardCode,
		};
		if (!isin.IsEmpty())
			securityId.Isin = isin;
		return securityId;
	}

	public static DateTime NormalizeUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();

	public static string ToNativeNumber(this decimal value)
		=> value.ToString("0.############################", CultureInfo.InvariantCulture);

	public static bool IsIsin(this string value)
	{
		if (value?.Length != 12 || !char.IsLetter(value[0]) || !char.IsLetter(value[1]) ||
			!char.IsDigit(value[^1]))
			return false;
		for (var i = 2; i < value.Length - 1; i++)
		{
			if (!char.IsLetterOrDigit(value[i]))
				return false;
		}
		return true;
	}
}
