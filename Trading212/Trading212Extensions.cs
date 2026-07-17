namespace StockSharp.Trading212;

static class Trading212Extensions
{
	public const string BoardCode = "T212";

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"STOCK" or "CVR" or "CORPACT" => SecurityTypes.Stock,
			"ETF" => SecurityTypes.Etf,
			"FOREX" => SecurityTypes.Currency,
			"FUTURES" => SecurityTypes.Future,
			"INDEX" => SecurityTypes.Index,
			"WARRANT" => SecurityTypes.Warrant,
			"CRYPTO" or "CRYPTOCURRENCY" => SecurityTypes.CryptoCurrency,
			_ => null,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static SecurityId ToSecurityId(this Trading212TradableInstrument instrument)
	{
		var securityId = new SecurityId
		{
			SecurityCode = instrument.Ticker,
			BoardCode = BoardCode,
		};
		if (!instrument.Isin.IsEmpty())
			securityId.Isin = instrument.Isin;
		return securityId;
	}

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy;

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOP_LIMIT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"LOCAL" or "UNCONFIRMED" or "CONFIRMED" => OrderStates.Pending,
			"NEW" or "CANCELLING" or "PARTIALLY_FILLED" or "REPLACING" => OrderStates.Active,
			"CANCELLED" or "FILLED" or "REPLACED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static string ToNativeTimeValidity(this DateTime? tillDate)
	{
		if (tillDate == null)
			return "GOOD_TILL_CANCEL";

		var expiry = tillDate.Value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(tillDate.Value, DateTimeKind.Utc)
			: tillDate.Value.ToUniversalTime();
		if (expiry.Date != DateTime.UtcNow.Date)
			throw new NotSupportedException(
				"Trading 212 supports DAY and GOOD_TILL_CANCEL orders, but not an explicit future expiry date.");
		return "DAY";
	}

	public static DateTime NormalizeUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
