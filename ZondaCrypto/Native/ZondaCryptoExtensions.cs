namespace StockSharp.ZondaCrypto.Native;

static class ZondaCryptoExtensions
{
	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency))
			.Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency))
				.Trim().ToUpperInvariant();

	public static string ToZondaMarketCode(
		this string securityCode)
	{
		securityCode = securityCode.ThrowIfEmpty(
			nameof(securityCode)).Trim();
		var parts = securityCode.Split(
			['/', '-', '_'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid zondacrypto market '{securityCode}'.");
		return $"{parts[0]}-{parts[1]}".ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(
		this ZondaCryptoMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.ZondaCrypto,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static string ToZonda(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"buy" or "bid" => Sides.Buy,
			"sell" or "ask" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"active" or "new" or "open" or "accepted" or
				"partially_filled" or "partially-filled" =>
				OrderStates.Active,
			"completed" or "filled" or "executed" or "done" or
				"cancelled" or "canceled" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime FromZondaTimestamp(
		this long timestamp)
		=> DateTimeOffset.FromUnixTimeMilliseconds(
			timestamp < 100_000_000_000
				? timestamp * 1000
				: timestamp).UtcDateTime;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static decimal? ToStep(this int precision)
	{
		if (precision < 0 || precision > 28)
			return null;
		var result = 1m;
		for (var i = 0; i < precision; i++)
			result /= 10m;
		return result;
	}
}
