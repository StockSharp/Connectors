namespace StockSharp.Buda.Native;

static class BudaExtensions
{
	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency))
			.Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency))
				.Trim().ToUpperInvariant();

	public static string ToBudaMarketId(this string securityCode)
	{
		securityCode = securityCode.ThrowIfEmpty(
			nameof(securityCode)).Trim();
		var parts = securityCode.Split(
			['/', '-', '_'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid Buda.com market '{securityCode}'.");
		return $"{parts[0]}-{parts[1]}".ToLowerInvariant();
	}

	public static string ToBudaChannelMarket(
		this string marketId)
		=> marketId.ThrowIfEmpty(nameof(marketId))
			.Replace("-", string.Empty)
			.Replace("/", string.Empty)
			.Replace("_", string.Empty)
			.ToLowerInvariant();

	public static SecurityId ToStockSharp(this BudaMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.Buda,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static string ToBuda(this Sides side)
		=> side switch
		{
			Sides.Buy => "Bid",
			Sides.Sell => "Ask",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"bid" or "buy" or "bids" => Sides.Buy,
			"ask" or "sell" or "asks" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"received" or "placed" or "pending" or "canceling" or
				"partially_filled" => OrderStates.Active,
			"traded" or "canceled" or "cancelled" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"ioc" => TimeInForce.MatchOrCancel,
			"fok" => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static string ToBudaOrderType(
		this TimeInForce? timeInForce,
		bool postOnly)
		=> postOnly
			? "post_only"
			: timeInForce switch
			{
				TimeInForce.MatchOrCancel => "ioc",
				TimeInForce.CancelBalance => "fok",
				_ => "gtc",
			};

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime FromBudaTimestamp(this long timestamp)
		=> DateTimeOffset.FromUnixTimeMilliseconds(
			timestamp < 100_000_000_000
				? timestamp * 1000
				: timestamp).UtcDateTime;
}
