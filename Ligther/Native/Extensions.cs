namespace StockSharp.Ligther.Native;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(2), "120" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromHours(6), "360" },
		{ TimeSpan.FromHours(8), "480" },
		{ TimeSpan.FromHours(12), "720" },
		{ TimeSpan.FromDays(1), "1D" },
		{ TimeSpan.FromDays(7), "1W" },
	};

	public static string ToNativeResolution(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string secCode, string boardCode)
	{
		if (secCode.IsEmpty())
			throw new ArgumentNullException(nameof(secCode));

		if (boardCode.IsEmpty())
			throw new ArgumentNullException(nameof(boardCode));

		return new()
		{
			SecurityCode = secCode.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}

	public static string ToNative(this SecurityId securityId)
		=> securityId.SecurityCode;

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OPEN" or "ACTIVE" or "PARTIAL" or "PARTIALLY_FILLED" => OrderStates.Active,
			"FILLED" or "CANCELLED" or "CANCELED" or "DONE" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static OrderTypes? ToOrderType(this string type)
		=> type?.ToUpperInvariant() switch
		{
			null => null,
			"LIMIT" => OrderTypes.Limit,
			"MARKET" => OrderTypes.Market,
			"STOP" or "TAKE_PROFIT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce? ToTimeInForce(this string tif)
		=> tif?.ToUpperInvariant() switch
		{
			null => null,
			"GTC" or "GOOD_TIL_CANCEL" => TimeInForce.PutInQueue,
			"IOC" or "IMMEDIATE_OR_CANCEL" => TimeInForce.CancelBalance,
			"FOK" or "FILL_OR_KILL" => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static string ToNative(this TimeInForce? tif)
		=> tif switch
		{
			null or TimeInForce.PutInQueue => "GTC",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => "GTC",
		};

	public static decimal GetStepByDecimals(int decimals)
		=> decimals <= 0 ? 1m : 1m / (decimal)Math.Pow(10, decimals);
}
