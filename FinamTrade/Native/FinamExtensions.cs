namespace StockSharp.FinamTrade.Native;

static class FinamExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(8),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
		TimeSpan.FromDays(90),
	];

	public static string ToNative(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "TIME_FRAME_M1"
			: timeFrame == TimeSpan.FromMinutes(5) ? "TIME_FRAME_M5"
			: timeFrame == TimeSpan.FromMinutes(15) ? "TIME_FRAME_M15"
			: timeFrame == TimeSpan.FromMinutes(30) ? "TIME_FRAME_M30"
			: timeFrame == TimeSpan.FromHours(1) ? "TIME_FRAME_H1"
			: timeFrame == TimeSpan.FromHours(2) ? "TIME_FRAME_H2"
			: timeFrame == TimeSpan.FromHours(4) ? "TIME_FRAME_H4"
			: timeFrame == TimeSpan.FromHours(8) ? "TIME_FRAME_H8"
			: timeFrame == TimeSpan.FromDays(1) ? "TIME_FRAME_D"
			: timeFrame == TimeSpan.FromDays(7) ? "TIME_FRAME_W"
			: timeFrame == TimeSpan.FromDays(30) ? "TIME_FRAME_MN"
			: timeFrame == TimeSpan.FromDays(90) ? "TIME_FRAME_QR"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Finam does not support the requested candle time frame.");

	public static string ToNativeSymbol(this SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var board = securityId.BoardCode.ThrowIfEmpty(nameof(securityId.BoardCode));
		return $"{code}@{board}";
	}

	public static SecurityId ToSecurityId(this string symbol)
	{
		var separator = symbol?.LastIndexOf('@') ?? -1;
		if (separator <= 0 || separator == symbol.Length - 1)
			return new() { SecurityCode = symbol, BoardCode = BoardCodes.Finam };

		return new()
		{
			SecurityCode = symbol[..separator],
			BoardCode = symbol[(separator + 1)..],
		};
	}

	public static SecurityTypes? ToSecurityType(this string type)
	{
		if (type.IsEmpty())
			return null;

		type = type.ToUpperInvariant();
		if (type.Contains("OPTION"))
			return SecurityTypes.Option;
		if (type.Contains("FUTURE"))
			return SecurityTypes.Future;
		if (type.Contains("BOND"))
			return SecurityTypes.Bond;
		if (type.Contains("ETF"))
			return SecurityTypes.Etf;
		if (type.Contains("FUND"))
			return SecurityTypes.Fund;
		if (type.Contains("CURRENCY") || type.Contains("FOREX") ||
			type.Equals("FX", StringComparison.Ordinal))
			return SecurityTypes.Currency;
		if (type.Contains("INDEX"))
			return SecurityTypes.Index;
		if (type.Contains("COMMOD"))
			return SecurityTypes.Commodity;
		if (type.Contains("STOCK") || type.Contains("EQUITY") ||
			type.Contains("SHARE"))
			return SecurityTypes.Stock;

		return null;
	}

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "SIDE_BUY" : "SIDE_SELL";

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("SIDE_SELL") ? Sides.Sell : Sides.Buy;

	public static string ToNative(this FinamTimeInForces value)
		=> value switch
		{
			FinamTimeInForces.Day => "TIME_IN_FORCE_DAY",
			FinamTimeInForces.GoodTillCancel => "TIME_IN_FORCE_GOOD_TILL_CANCEL",
			FinamTimeInForces.GoodTillCrossing => "TIME_IN_FORCE_GOOD_TILL_CROSSING",
			FinamTimeInForces.Extended => "TIME_IN_FORCE_EXT",
			FinamTimeInForces.OnOpen => "TIME_IN_FORCE_ON_OPEN",
			FinamTimeInForces.OnClose => "TIME_IN_FORCE_ON_CLOSE",
			FinamTimeInForces.ImmediateOrCancel => "TIME_IN_FORCE_IOC",
			FinamTimeInForces.FillOrKill => "TIME_IN_FORCE_FOK",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToNative(this FinamStopConditions value)
		=> value == FinamStopConditions.LastDown
			? "STOP_CONDITION_LAST_DOWN"
			: "STOP_CONDITION_LAST_UP";

	public static OrderTypes ToOrderType(this string value)
		=> value switch
		{
			"ORDER_TYPE_MARKET" => OrderTypes.Market,
			"ORDER_TYPE_STOP" or "ORDER_TYPE_STOP_LIMIT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
		=> value switch
		{
			"ORDER_STATUS_NEW" or
			"ORDER_STATUS_PARTIALLY_FILLED" or
			"ORDER_STATUS_SUSPENDED" or
			"ORDER_STATUS_WATCHING" => OrderStates.Active,

			"ORDER_STATUS_PENDING_CANCEL" or
			"ORDER_STATUS_PENDING_NEW" or
			"ORDER_STATUS_UNSPECIFIED" or
			"ORDER_STATUS_FORWARDING" or
			"ORDER_STATUS_WAIT" or
			"ORDER_STATUS_LINK_WAIT" or
			"ORDER_STATUS_SL_GUARD_TIME" or
			"ORDER_STATUS_SL_FORWARDING" or
			"ORDER_STATUS_TP_GUARD_TIME" or
			"ORDER_STATUS_TP_CORRECTION" or
			"ORDER_STATUS_TP_FORWARDING" or
			"ORDER_STATUS_TP_CORR_GUARD_TIME" => OrderStates.Pending,

			"ORDER_STATUS_REJECTED" or
			"ORDER_STATUS_FAILED" or
			"ORDER_STATUS_DENIED_BY_BROKER" or
			"ORDER_STATUS_REJECTED_BY_EXCHANGE" => OrderStates.Failed,

			"ORDER_STATUS_FILLED" or
			"ORDER_STATUS_DONE_FOR_DAY" or
			"ORDER_STATUS_CANCELED" or
			"ORDER_STATUS_REPLACED" or
			"ORDER_STATUS_EXPIRED" or
			"ORDER_STATUS_EXECUTED" or
			"ORDER_STATUS_DISABLED" or
			"ORDER_STATUS_SL_EXECUTED" or
			"ORDER_STATUS_TP_EXECUTED" => OrderStates.Done,

			_ => OrderStates.Pending,
		};

	public static decimal? ToDecimal(this FinamDecimal value)
		=> decimal.TryParse(value?.Value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static FinamDecimal ToNativeDecimal(this decimal value)
		=> new() { Value = value.ToString(CultureInfo.InvariantCulture) };

	public static decimal ToDecimal(this FinamMoney value)
	{
		if (!long.TryParse(value?.Units, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var units))
			units = 0;
		return units + (value?.Nanos ?? 0) / 1_000_000_000m;
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value.EqualsIgnoreCase("RUR") ? "RUB" : value,
			true, out var currency)
				? currency
				: null;
}
