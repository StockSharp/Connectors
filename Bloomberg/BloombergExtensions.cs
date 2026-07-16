namespace StockSharp.Bloomberg;

internal static class BloombergExtensions
{
	public static SecurityTypes? ToSecurityType(this string securityType, string marketSector)
	{
		var type = securityType?.ToUpperInvariant();
		if (type?.Contains("ETF", StringComparison.Ordinal) == true)
			return SecurityTypes.Etf;
		if (type?.Contains("FUND", StringComparison.Ordinal) == true)
			return SecurityTypes.Fund;
		if (type?.Contains("OPTION", StringComparison.Ordinal) == true)
			return SecurityTypes.Option;
		if (type?.Contains("FUTURE", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (type?.Contains("BOND", StringComparison.Ordinal) == true || type?.Contains("NOTE", StringComparison.Ordinal) == true)
			return SecurityTypes.Bond;
		if (type?.Contains("INDEX", StringComparison.Ordinal) == true)
			return SecurityTypes.Index;

		return marketSector?.ToUpperInvariant() switch
		{
			"EQUITY" => SecurityTypes.Stock,
			"COMDTY" => SecurityTypes.Commodity,
			"CURNCY" => SecurityTypes.Currency,
			"INDEX" => SecurityTypes.Index,
			"MTGE" or "M-MKT" or "GOVT" or "CORP" or "MUNI" => SecurityTypes.Bond,
			_ => null,
		};
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CALL" or "C" => OptionTypes.Call,
			"PUT" or "P" => OptionTypes.Put,
			_ => null,
		};

	public static string ToNative(this Sides side, OrderPositionEffects? positionEffect)
		=> (side, positionEffect) switch
		{
			(Sides.Buy, OrderPositionEffects.CloseOnly) => "COVR",
			(Sides.Sell, OrderPositionEffects.OpenOnly) => "SHRT",
			(Sides.Buy, _) => "BUY",
			_ => "SELL",
		};

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"BUY" or "COVR" => Sides.Buy,
			_ => Sides.Sell,
		};

	public static OrderPositionEffects? ToPositionEffect(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"SHRT" or "SHORT" => OrderPositionEffects.OpenOnly,
			"COVR" or "COVER" => OrderPositionEffects.CloseOnly,
			_ => null,
		};

	public static string ToNative(this OrderTypes? type, decimal? stopPrice)
		=> type switch
		{
			null or OrderTypes.Market when stopPrice == null => "MKT",
			null or OrderTypes.Market when stopPrice != null => "STP",
			OrderTypes.Limit when stopPrice == null => "LMT",
			OrderTypes.Conditional when stopPrice != null => "STP",
			OrderTypes.Limit when stopPrice != null => "STPLMT",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this string type)
		=> type?.ToUpperInvariant() switch
		{
			"MKT" => OrderTypes.Market,
			"LMT" or "STPLMT" => OrderTypes.Limit,
			"STP" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToNative(this TimeInForce? timeInForce, DateTime? tillDate)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ when tillDate == null => "GTC",
			_ when tillDate.Value.Date <= DateTime.UtcNow.Date => "DAY",
			_ => "GTD",
		};

	public static TimeInForce ToTimeInForce(this string timeInForce)
		=> timeInForce?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"FILLED" or "CANCEL" or "CANCELLED" or "DONE" or "CXLREP" => OrderStates.Done,
			"REJECTED" or "REJECT" or "ERROR" or "ROUTE-ERR" => OrderStates.Failed,
			"NEW" or "SENT" or "WORKING" or "PARTFILL" or "PARTIALLY FILLED" or "ASSIGN" or "CXLREQ" => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static string ToBoardCode(this string exchange)
		=> exchange.IsEmpty() ? "BLOOMBERG" : exchange.ToUpperInvariant();
}
