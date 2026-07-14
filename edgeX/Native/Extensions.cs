namespace StockSharp.EdgeX.Native;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "MINUTE_1" },
		{ TimeSpan.FromMinutes(5), "MINUTE_5" },
		{ TimeSpan.FromMinutes(15), "MINUTE_15" },
		{ TimeSpan.FromMinutes(30), "MINUTE_30" },
		{ TimeSpan.FromHours(1), "HOUR_1" },
		{ TimeSpan.FromHours(2), "HOUR_2" },
		{ TimeSpan.FromHours(4), "HOUR_4" },
		{ TimeSpan.FromHours(6), "HOUR_6" },
		{ TimeSpan.FromHours(8), "HOUR_8" },
		{ TimeSpan.FromHours(12), "HOUR_12" },
		{ TimeSpan.FromDays(1), "DAY_1" },
		{ TimeSpan.FromDays(7), "WEEK_1" },
		{ TimeSpan.FromDays(30), "MONTH_1" },
	};

	public static string ToNativeKlineType(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string klineType)
		=> TimeFrames.TryGetKey2(klineType) ?? throw new ArgumentOutOfRangeException(nameof(klineType), klineType, LocalizedStrings.InvalidValue);

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
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"BUY" or "BID" or "LONG" => Sides.Buy,
			"SELL" or "ASK" or "SHORT" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this TimeInForce? tif)
		=> tif switch
		{
			null or TimeInForce.PutInQueue => "GOOD_TIL_CANCEL",
			TimeInForce.CancelBalance => "IMMEDIATE_OR_CANCEL",
			TimeInForce.MatchOrCancel => "FILL_OR_KILL",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToTimeInForce(this string tif)
		=> tif?.ToUpperInvariant() switch
		{
			null => null,
			"GOOD_TIL_CANCEL" or "GTC" => TimeInForce.PutInQueue,
			"IMMEDIATE_OR_CANCEL" or "IOC" => TimeInForce.CancelBalance,
			"FILL_OR_KILL" or "FOK" => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OPEN" or "NEW" or "PARTIALLY_FILLED" or "PENDING" => OrderStates.Active,
			"FILLED" or "CANCELED" or "CANCELLED" or "EXPIRED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static OrderTypes? ToOrderType(this string type)
		=> type?.ToUpperInvariant() switch
		{
			null => null,
			"LIMIT" => OrderTypes.Limit,
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOP_MARKET" or "TAKE_PROFIT" or "TAKE_PROFIT_MARKET" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToNative(this OrderTypes? orderType, EdgeXOrderCondition condition)
	{
		switch (orderType ?? OrderTypes.Limit)
		{
			case OrderTypes.Limit:
				return "LIMIT";

			case OrderTypes.Market:
				return "MARKET";

			case OrderTypes.Conditional:
				if (condition is null)
					throw new InvalidOperationException("Conditional order requires EdgeXOrderCondition.");

				return condition.Type == EdgeXOrderConditionTypes.TakeProfit ? "TAKE_PROFIT" : "STOP";

			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		}
	}
}
