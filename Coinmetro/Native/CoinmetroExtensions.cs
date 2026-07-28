namespace StockSharp.Coinmetro.Native;

static class CoinmetroExtensions
{
	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency))
			.Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency))
				.Trim().ToUpperInvariant();

	public static SecurityId ToStockSharp(
		this CoinmetroMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.Coinmetro,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static decimal? ToStep(this int precision)
	{
		if (precision < 0 || precision > 28)
			return null;
		var result = 1m;
		for (var i = 0; i < precision; i++)
			result /= 10m;
		return result;
	}

	public static OrderTypes ToOrderType(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToTimeInForce(this int value)
		=> value switch
		{
			2 => TimeInForce.MatchOrCancel,
			4 => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static int ToCoinmetro(
		this TimeInForce? value,
		DateTime? tillDate)
		=> tillDate is not null
			? 3
			: value switch
			{
				TimeInForce.MatchOrCancel => 2,
				TimeInForce.CancelBalance => 4,
				_ => 1,
			};

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime FromCoinmetroTimestamp(
		this long timestamp)
		=> DateTimeOffset.FromUnixTimeMilliseconds(
			timestamp < 100_000_000_000
				? timestamp * 1000
				: timestamp).UtcDateTime;
}
