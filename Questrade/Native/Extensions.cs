namespace StockSharp.Questrade.Native;

static class QuestradeExtensions
{
	private static readonly KeyValuePair<TimeSpan, string>[] _timeFrames =
	[
		new(TimeSpan.FromMinutes(1), "OneMinute"),
		new(TimeSpan.FromMinutes(2), "TwoMinutes"),
		new(TimeSpan.FromMinutes(3), "ThreeMinutes"),
		new(TimeSpan.FromMinutes(4), "FourMinutes"),
		new(TimeSpan.FromMinutes(5), "FiveMinutes"),
		new(TimeSpan.FromMinutes(10), "TenMinutes"),
		new(TimeSpan.FromMinutes(15), "FifteenMinutes"),
		new(TimeSpan.FromMinutes(20), "TwentyMinutes"),
		new(TimeSpan.FromMinutes(30), "HalfHour"),
		new(TimeSpan.FromHours(1), "OneHour"),
		new(TimeSpan.FromHours(2), "TwoHours"),
		new(TimeSpan.FromHours(4), "FourHours"),
		new(TimeSpan.FromDays(1), "OneDay"),
		new(TimeSpan.FromDays(7), "OneWeek"),
		new(TimeSpan.FromDays(30), "OneMonth"),
		new(TimeSpan.FromDays(365), "OneYear"),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Select(p => p.Key);

	public static string ToInterval(this TimeSpan timeFrame)
		=> _timeFrames.FirstOrDefault(p => p.Key == timeFrame).Value
			?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Questrade candle interval.");

	public static long ToSymbolId(this SecurityId securityId)
	{
		var symbolId = securityId.Native switch
		{
			long value => value,
			int value => value,
			decimal value when value <= long.MaxValue => (long)value,
			string value when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0,
		};
		if (symbolId <= 0 && !long.TryParse(securityId.SecurityCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out symbolId))
			throw new InvalidOperationException("Questrade security requires a numeric symbol id in SecurityId.Native or SecurityCode.");
		return symbolId;
	}

	public static SecurityId ToSecurityId(this QuestradeSymbol symbol)
		=> new()
		{
			SecurityCode = symbol.Symbol.IsEmpty(symbol.SymbolId.ToString(CultureInfo.InvariantCulture)),
			BoardCode = symbol.ListingExchange.IsEmpty("QUESTRADE"),
			Native = symbol.SymbolId,
		};

	public static SecurityId ToSecurityId(this QuestradeSymbolSearchItem symbol)
		=> new()
		{
			SecurityCode = symbol.Symbol.IsEmpty(symbol.SymbolId.ToString(CultureInfo.InvariantCulture)),
			BoardCode = symbol.ListingExchange.IsEmpty("QUESTRADE"),
			Native = symbol.SymbolId,
		};

	public static SecurityTypes ToSecurityType(this string securityType)
		=> securityType?.ToUpperInvariant() switch
		{
			"OPTION" => SecurityTypes.Option,
			"BOND" => SecurityTypes.Bond,
			"RIGHT" => SecurityTypes.Warrant,
			"GOLD" => SecurityTypes.Commodity,
			"MUTUALFUND" => SecurityTypes.Fund,
			"INDEX" => SecurityTypes.Index,
			_ => SecurityTypes.Stock,
		};

	public static string ToNativeOrderType(this OrderTypes orderType, decimal price, QuestradeOrderCondition condition)
	{
		if (orderType == OrderTypes.Market)
			return "Market";
		if (orderType == OrderTypes.Conditional)
			return condition?.StopPrice is > 0 && price > 0 ? "StopLimit" : "Stop";
		return "Limit";
	}

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOPLIMIT" or "TRAILSTOPINPERCENTAGE" or "TRAILSTOPINDOLLAR" or
				"TRAILSTOPLIMITINPERCENTAGE" or "TRAILSTOPLIMITINDOLLAR" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToNativeDuration(this TimeInForce? timeInForce, DateTimeOffset? tillDate,
		QuestradeOrderCondition condition)
	{
		if (tillDate != null)
			return "GoodTillDate";
		return timeInForce switch
		{
			TimeInForce.CancelBalance => "ImmediateOrCancel",
			TimeInForce.MatchOrCancel => "FillOrKill",
			_ => (condition?.Duration ?? QuestradeOrderDurations.Day).ToString(),
		};
	}

	public static TimeInForce ToTimeInForce(this string duration)
		=> duration?.ToUpperInvariant() switch
		{
			"IMMEDIATEORCANCEL" => TimeInForce.CancelBalance,
			"FILLORKILL" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static string ToNativeSide(this Sides side, QuestradeOrderCondition condition)
		=> condition?.NativeSide switch
		{
			QuestradeOrderSides.Short => "Short",
			QuestradeOrderSides.Cover => "Cov",
			QuestradeOrderSides.BuyToOpen => "BTO",
			QuestradeOrderSides.SellToClose => "STC",
			QuestradeOrderSides.SellToOpen => "STO",
			QuestradeOrderSides.BuyToClose => "BTC",
			_ => side == Sides.Buy ? "Buy" : "Sell",
		};

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() is "BUY" or "BTO" or "BTC" or "COV" ? Sides.Buy : Sides.Sell;

	public static QuestradeOrderSides? ToNativeSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"SHORT" => QuestradeOrderSides.Short,
			"COV" => QuestradeOrderSides.Cover,
			"BTO" => QuestradeOrderSides.BuyToOpen,
			"STC" => QuestradeOrderSides.SellToClose,
			"STO" => QuestradeOrderSides.SellToOpen,
			"BTC" => QuestradeOrderSides.BuyToClose,
			"BUY" => QuestradeOrderSides.Buy,
			"SELL" => QuestradeOrderSides.Sell,
			_ => null,
		};

	public static OrderStates ToOrderState(this string state)
		=> state?.ToUpperInvariant() switch
		{
			"FAILED" or "REJECTED" => OrderStates.Failed,
			"CANCELED" or "PARTIALCANCELED" or "EXECUTED" or "STOPPED" or "EXPIRED" => OrderStates.Done,
			"ACCEPTED" or "PARTIAL" or "REPLACED" or "SUSPENDED" or "QUEUED" or "TRIGGERED" or
				"ACTIVATED" or "CONTINGENTORDER" => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static bool IsFailed(this string state)
		=> state?.ToUpperInvariant() is "FAILED" or "REJECTED";

	public static decimal? TotalCommission(this QuestradeExecution execution)
	{
		if (execution == null)
			return null;
		var values = new[] { execution.Commission, execution.ExecutionFee, execution.SecFee, execution.CanadianExecutionFee };
		return values.Any(v => v != null) ? values.Sum(v => v ?? 0) : null;
	}
}
