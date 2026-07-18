namespace StockSharp.CryptoCom.Native;

static class CryptoComExtensions
{
	public static IReadOnlyDictionary<TimeSpan, string> TimeFrames { get; } =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(2)] = "2h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1D",
			[TimeSpan.FromDays(7)] = "7D",
			[TimeSpan.FromDays(14)] = "14D",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string securityCode, string boardCode)
	{
		if (securityCode.IsEmpty())
			throw new ArgumentNullException(nameof(securityCode));
		if (boardCode.IsEmpty())
			throw new ArgumentNullException(nameof(boardCode));

		return new()
		{
			SecurityCode = securityCode.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static int? ToInt(this string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static long ToUnixMilliseconds(this DateTime time)
		=> (long)time.ToUniversalTime().ToUnix(false);

	public static CryptoComSides ToNative(this Sides side)
		=> side switch
		{
			Sides.Buy => CryptoComSides.Buy,
			Sides.Sell => CryptoComSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToStockSharp(this CryptoComSides side)
		=> side switch
		{
			CryptoComSides.Buy => Sides.Buy,
			CryptoComSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static CryptoComTimeInForces ToNative(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue => CryptoComTimeInForces.GoodTillCancel,
			TimeInForce.CancelBalance => CryptoComTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => CryptoComTimeInForces.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToStockSharp(this CryptoComTimeInForces? timeInForce)
		=> timeInForce switch
		{
			CryptoComTimeInForces.GoodTillCancel => TimeInForce.PutInQueue,
			CryptoComTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			CryptoComTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			null => null,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToStockSharp(this CryptoComOrderStatuses status)
		=> status switch
		{
			CryptoComOrderStatuses.Pending or CryptoComOrderStatuses.New or CryptoComOrderStatuses.Active => OrderStates.Active,
			CryptoComOrderStatuses.Filled or CryptoComOrderStatuses.Canceled or CryptoComOrderStatuses.Expired => OrderStates.Done,
			CryptoComOrderStatuses.Rejected => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	public static CryptoComTriggerPriceTypesNative ToNative(this CryptoComTriggerPriceTypes type)
		=> type switch
		{
			CryptoComTriggerPriceTypes.Last => CryptoComTriggerPriceTypesNative.LastPrice,
			CryptoComTriggerPriceTypes.Mark => CryptoComTriggerPriceTypesNative.MarkPrice,
			CryptoComTriggerPriceTypes.Index => CryptoComTriggerPriceTypesNative.IndexPrice,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static CryptoComTriggerPriceTypes ToStockSharp(this CryptoComTriggerPriceTypesNative type)
		=> type switch
		{
			CryptoComTriggerPriceTypesNative.LastPrice => CryptoComTriggerPriceTypes.Last,
			CryptoComTriggerPriceTypesNative.MarkPrice => CryptoComTriggerPriceTypes.Mark,
			CryptoComTriggerPriceTypesNative.IndexPrice => CryptoComTriggerPriceTypes.Index,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComSides value)
		=> value switch
		{
			CryptoComSides.Buy => "BUY",
			CryptoComSides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComOrderTypes value)
		=> value switch
		{
			CryptoComOrderTypes.Limit => "LIMIT",
			CryptoComOrderTypes.Market => "MARKET",
			CryptoComOrderTypes.StopLoss => "STOP_LOSS",
			CryptoComOrderTypes.StopLimit => "STOP_LIMIT",
			CryptoComOrderTypes.TakeProfit => "TAKE_PROFIT",
			CryptoComOrderTypes.TakeProfitLimit => "TAKE_PROFIT_LIMIT",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComTimeInForces value)
		=> value switch
		{
			CryptoComTimeInForces.GoodTillCancel => "GOOD_TILL_CANCEL",
			CryptoComTimeInForces.ImmediateOrCancel => "IMMEDIATE_OR_CANCEL",
			CryptoComTimeInForces.FillOrKill => "FILL_OR_KILL",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComExecutionInstructions value)
		=> value switch
		{
			CryptoComExecutionInstructions.PostOnly => "POST_ONLY",
			CryptoComExecutionInstructions.ReduceOnly => "REDUCE_ONLY",
			CryptoComExecutionInstructions.SmartPostOnly => "SMART_POST_ONLY",
			CryptoComExecutionInstructions.Liquidation => "LIQUIDATION",
			CryptoComExecutionInstructions.IsolatedMargin => "ISOLATED_MARGIN",
			CryptoComExecutionInstructions.MarginOrder => "MARGIN_ORDER",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComTriggerPriceTypesNative value)
		=> value switch
		{
			CryptoComTriggerPriceTypesNative.LastPrice => "LAST_PRICE",
			CryptoComTriggerPriceTypesNative.MarkPrice => "MARK_PRICE",
			CryptoComTriggerPriceTypesNative.IndexPrice => "INDEX_PRICE",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComSpotMarginModes value)
		=> value switch
		{
			CryptoComSpotMarginModes.Spot => "SPOT",
			CryptoComSpotMarginModes.Margin => "MARGIN",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this CryptoComCancelOrderTypes value)
		=> value switch
		{
			CryptoComCancelOrderTypes.Limit => "LIMIT",
			CryptoComCancelOrderTypes.Trigger => "TRIGGER",
			CryptoComCancelOrderTypes.All => "ALL",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
}
