namespace StockSharp.Gemini.Native;

static class GeminiExtensions
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

	public static string ToGeminiInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1hr"
			: timeFrame == TimeSpan.FromHours(6) ? "6hr"
			: timeFrame == TimeSpan.FromDays(1) ? "1day"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Gemini candle interval.");

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			BoardCode = BoardCodes.Gemini,
		};

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime FromNanoseconds(this long value)
		=> DateTime.SpecifyKind(DateTime.UnixEpoch.AddTicks(value / 100),
			DateTimeKind.Utc);

	public static long ToMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static Sides ToStockSharp(this GeminiSides side)
		=> side == GeminiSides.Buy ? Sides.Buy : Sides.Sell;

	public static Sides ToStockSharp(this GeminiWsSides side)
		=> side == GeminiWsSides.Buy ? Sides.Buy : Sides.Sell;

	public static Sides ToStockSharp(this GeminiTradeSides side)
		=> side == GeminiTradeSides.Buy ? Sides.Buy : Sides.Sell;

	public static GeminiWsSides ToGemini(this Sides side)
		=> side == Sides.Buy ? GeminiWsSides.Buy : GeminiWsSides.Sell;

	public static OrderTypes ToStockSharp(this GeminiRestOrderTypes type)
		=> type is GeminiRestOrderTypes.Market or GeminiRestOrderTypes.StopMarket
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderTypes ToStockSharp(this GeminiWsOrderTypes? type)
		=> type is GeminiWsOrderTypes.Market or GeminiWsOrderTypes.StopMarket
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderStates ToStockSharp(this GeminiWsOrderStatuses status)
		=> status switch
		{
			GeminiWsOrderStatuses.New or GeminiWsOrderStatuses.Open or
				GeminiWsOrderStatuses.PartiallyFilled or GeminiWsOrderStatuses.Modified =>
				OrderStates.Active,
			GeminiWsOrderStatuses.Filled or GeminiWsOrderStatuses.Canceled =>
				OrderStates.Done,
			GeminiWsOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static GeminiWsTimeInForces ToGemini(this TimeInForce? timeInForce,
		bool isPostOnly)
		=> isPostOnly
			? GeminiWsTimeInForces.MakerOrCancel
			: timeInForce switch
			{
				TimeInForce.CancelBalance => GeminiWsTimeInForces.ImmediateOrCancel,
				TimeInForce.MatchOrCancel => GeminiWsTimeInForces.FillOrKill,
				_ => GeminiWsTimeInForces.GoodTillCanceled,
			};

	public static SecurityStates ToStockSharp(this GeminiSymbolStatuses status)
		=> status is GeminiSymbolStatuses.Open or GeminiSymbolStatuses.PostOnly or
			GeminiSymbolStatuses.LimitOnly
			? SecurityStates.Trading
			: SecurityStates.Stoped;
}
