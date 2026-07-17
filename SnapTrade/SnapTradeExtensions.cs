namespace StockSharp.SnapTrade;

static class SnapTradeExtensions
{
	public const string BoardCode = "SNAPTRADE";

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"stock" or "cs" or "ad" or "ps" or "rt" or "ut" or "wi" or "wt" => SecurityTypes.Stock,
			"etf" or "et" => SecurityTypes.Etf,
			"mutualfund" or "oef" or "cef" => SecurityTypes.Fund,
			"option" => SecurityTypes.Option,
			"future" => SecurityTypes.Future,
			"crypto" => SecurityTypes.CryptoCurrency,
			"cfd" => SecurityTypes.Cfd,
			"bnd" => SecurityTypes.Bond,
			_ => null,
		};

	public static Sides ToSide(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"SELL" or "SELL_SHORT" or "SELL_OPEN" or "SELL_CLOSE" or
				"SELL_TO_OPEN" or "SELL_TO_CLOSE" => Sides.Sell,
			_ => Sides.Buy,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"ACCEPTED" or "PARTIAL" or "QUEUED" or "TRIGGERED" or "ACTIVATED" or
				"CONTINGENT_ORDER" => OrderStates.Active,
			"EXECUTED" or "CANCELED" or "PARTIAL_CANCELED" or "REPLACED" or
				"EXPIRED" => OrderStates.Done,
			"FAILED" or "REJECTED" or "STOPPED" or "SUSPENDED" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"limit" => OrderTypes.Limit,
			"stop" or "stoplimit" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static DateTime NormalizeUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();

	public static string ToNativeNumber(this decimal value)
		=> value.ToString("0.############################", CultureInfo.InvariantCulture);

	public static SecurityId ToSecurityId(this SnapTradeUniversalSymbol symbol)
	{
		var id = new SecurityId
		{
			SecurityCode = symbol?.Symbol.IsEmpty(symbol?.RawSymbol),
			BoardCode = symbol?.Exchange?.MicCode.IsEmpty(symbol?.Exchange?.Code)
				.IsEmpty(BoardCode),
		};
		if (symbol?.FigiCode.IsEmpty() == false)
			id.Bloomberg = symbol.FigiCode;
		return id;
	}

	public static SecurityId ToSecurityId(this SnapTradePositionInstrument instrument)
	{
		var id = new SecurityId
		{
			SecurityCode = instrument?.Symbol,
			BoardCode = instrument?.Exchange.IsEmpty(BoardCode),
		};
		if (instrument?.FigiInstrument?.FigiCode.IsEmpty() == false)
			id.Bloomberg = instrument.FigiInstrument.FigiCode;
		return id;
	}

	public static SecurityId ToSecurityId(this SnapTradeOrder order)
		=> order?.UniversalSymbol != null
			? order.UniversalSymbol.ToSecurityId()
			: new SecurityId
			{
				SecurityCode = order?.OptionSymbol?.Ticker.IsEmpty(order?.Symbol),
				BoardCode = BoardCode,
			};
}
