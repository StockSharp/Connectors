namespace StockSharp.VALR.Native;

static class VALRExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(6),
		TimeSpan.FromDays(1),
	];

	public static string NormalizeSymbol(this string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.Valr,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static VALRSides ToVALR(this Sides side)
		=> side switch
		{
			Sides.Buy => VALRSides.Buy,
			Sides.Sell => VALRSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this VALRSides side)
		=> side switch
		{
			VALRSides.Buy => Sides.Buy,
			VALRSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static VALRTimeInForce ToVALR(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue =>
				VALRTimeInForce.GoodTillCancelled,
			TimeInForce.CancelBalance => VALRTimeInForce.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => VALRTimeInForce.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, "VALR supports GTC, IOC, and FOK only."),
		};

	public static TimeInForce ToStockSharp(this VALRTimeInForce timeInForce)
		=> timeInForce switch
		{
			VALRTimeInForce.GoodTillCancelled => TimeInForce.PutInQueue,
			VALRTimeInForce.ImmediateOrCancel => TimeInForce.CancelBalance,
			VALRTimeInForce.FillOrKill => TimeInForce.MatchOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	public static OrderTypes ToStockSharp(this VALROrderTypes type)
		=> type switch
		{
			VALROrderTypes.Market or VALROrderTypes.Simple => OrderTypes.Market,
			VALROrderTypes.StopLossLimit or
				VALROrderTypes.TakeProfitLimit => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this VALROrderStatuses status)
		=> status switch
		{
			VALROrderStatuses.Placed or VALROrderStatuses.Active or
				VALROrderStatuses.PartiallyFilled or
				VALROrderStatuses.OrderModified => OrderStates.Active,
			VALROrderStatuses.Failed => OrderStates.Failed,
			_ => OrderStates.Done,
		};

	public static int ToVALRPeriod(this TimeSpan timeFrame)
	{
		var seconds = timeFrame.TotalSeconds.To<int>();
		if (!TimeFrames.Any(value => value.TotalSeconds.To<int>() == seconds))
			throw new NotSupportedException(
				$"VALR does not support the {timeFrame} candle interval.");
		return seconds;
	}

	public static string ToWire(this decimal value)
		=> value.ToString("0.#############################",
			CultureInfo.InvariantCulture);

	public static DateTime ToVALRTime(this string value, DateTime fallback)
	{
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var timestamp))
			return timestamp.UtcDateTime;
		return fallback.Kind == DateTimeKind.Utc
			? fallback
			: fallback.ToUniversalTime();
	}

	public static string ToQueryValue(this VALROrderStatuses status)
		=> status switch
		{
			VALROrderStatuses.Placed => "PLACED",
			VALROrderStatuses.Active => "ACTIVE",
			VALROrderStatuses.PartiallyFilled => "PARTIALLY_FILLED",
			VALROrderStatuses.PartiallyFilledDueToSlippage =>
				"PARTIALLY_FILLED_DUE_TO_SLIPPAGE",
			VALROrderStatuses.OrderModified => "ORDER_MODIFIED",
			VALROrderStatuses.Filled => "FILLED",
			VALROrderStatuses.Cancelled => "CANCELLED",
			VALROrderStatuses.Expired => "EXPIRED",
			VALROrderStatuses.Failed => "FAILED",
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				null),
		};
}
