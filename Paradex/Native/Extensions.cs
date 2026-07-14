namespace StockSharp.Paradex.Native;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "1w" },
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
			"BUY" => Sides.Buy,
			"SELL" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
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

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OPEN" or "PENDING" or "PARTIALLY_FILLED" => OrderStates.Active,
			"FILLED" or "CANCELED" or "CANCELLED" or "EXPIRED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static TimeInForce? ToTimeInForce(this string tif)
		=> tif?.ToUpperInvariant() switch
		{
			null => null,
			"GTC" => TimeInForce.PutInQueue,
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
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
}
