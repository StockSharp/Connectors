namespace StockSharp.AltCoinTrader.Native;

static class AltCoinTraderExtensions
{
	public static string ToAltCoinTraderSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(
			['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		return parts.Length switch
		{
			1 => parts[0].ToUpperInvariant(),
			2 => (parts[0] + parts[1]).ToUpperInvariant(),
			_ => throw new FormatException(
				$"Invalid AltCoinTrader symbol '{value}'."),
		};
	}

	public static string ToAltCoinTraderSecurityCode(
		this string value)
		=> value.ToAltCoinTraderSymbol();

	public static SecurityId ToAltCoinTraderSecurityId(
		this string value)
		=> new()
		{
			SecurityCode = value.ToAltCoinTraderSecurityCode(),
			BoardCode = BoardCodes.AltCoinTrader,
		};

	public static SecurityId ToStockSharp(
		this AltCoinTraderMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.AltCoinTrader,
		};

	public static DateTime FromAltCoinTraderSeconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToAltCoinTraderSeconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalSeconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(
				value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < precision; index++)
			step /= 10m;
		return step;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToAltCoinTrader(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this string type)
		=> type?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"stop_limit" or "stop_market" =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.Trim().ToLowerInvariant() switch
		{
			"open" or "partially_filled" => OrderStates.Active,
			"filled" or "cancelled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static string ToAltCoinTrader(
		this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue => "GTC",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => throw new ArgumentOutOfRangeException(
				nameof(timeInForce),
				timeInForce,
				"Unsupported AltCoinTrader time in force."),
		};

	public static string ToAltCoinTrader(
		this TimeInForce timeInForce)
		=> ((TimeInForce?)timeInForce).ToAltCoinTrader();

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientOrderId(long transactionId)
		=> "s" + Math.Abs(transactionId).ToString(
			"D10", CultureInfo.InvariantCulture);

	public static long? ParseClientOrderId(string value)
		=> value?.Length > 1 &&
			(value[0] is 's' or 'S') &&
			long.TryParse(
				value.AsSpan(1),
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var result)
					? result
					: null;
}
