namespace StockSharp.Luno.Native;

static class LunoExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(3),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(8),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(3),
		TimeSpan.FromDays(7),
	];

	public static string NormalizeSymbol(this string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.Luno,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static LunoLimitSides ToLunoLimit(this Sides side)
		=> side switch
		{
			Sides.Buy => LunoLimitSides.Bid,
			Sides.Sell => LunoLimitSides.Ask,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static LunoSides ToLuno(this Sides side)
		=> side switch
		{
			Sides.Buy => LunoSides.Buy,
			Sides.Sell => LunoSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this LunoSides side)
		=> side switch
		{
			LunoSides.Buy => Sides.Buy,
			LunoSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this LunoLimitSides side)
		=> side switch
		{
			LunoLimitSides.Bid => Sides.Buy,
			LunoLimitSides.Ask => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static LunoTimeInForce ToLuno(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue =>
				LunoTimeInForce.GoodTillCancelled,
			TimeInForce.CancelBalance => LunoTimeInForce.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => LunoTimeInForce.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, "Luno supports GTC, IOC, and FOK only."),
		};

	public static TimeInForce ToStockSharp(this LunoTimeInForce timeInForce)
		=> timeInForce switch
		{
			LunoTimeInForce.GoodTillCancelled => TimeInForce.PutInQueue,
			LunoTimeInForce.ImmediateOrCancel => TimeInForce.CancelBalance,
			LunoTimeInForce.FillOrKill => TimeInForce.MatchOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	public static OrderTypes ToStockSharp(this LunoOrderTypes type)
		=> type switch
		{
			LunoOrderTypes.Market => OrderTypes.Market,
			LunoOrderTypes.StopLimit => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this LunoOrderStatuses status)
		=> status switch
		{
			LunoOrderStatuses.Awaiting or LunoOrderStatuses.Pending =>
				OrderStates.Active,
			_ => OrderStates.Done,
		};

	public static SecurityStates ToStockSharp(this LunoTradingStatuses status)
		=> status == LunoTradingStatuses.Active
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static SecurityStates ToStockSharp(this LunoTickerStatuses status)
		=> status == LunoTickerStatuses.Active
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static SecurityStates ToStockSharp(this LunoStreamStatuses status)
		=> status == LunoStreamStatuses.Active
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static int ToLunoDuration(this TimeSpan timeFrame)
	{
		var seconds = timeFrame.TotalSeconds.To<int>();
		if (!TimeFrames.Any(value => value.TotalSeconds.To<int>() == seconds))
			throw new NotSupportedException(
				$"Luno does not support the {timeFrame} candle interval.");
		return seconds;
	}

	public static string ToWire(this decimal value)
		=> value.ToString("0.#############################",
			CultureInfo.InvariantCulture);

	public static string ToWire(this LunoLimitSides side)
		=> side switch
		{
			LunoLimitSides.Bid => "BID",
			LunoLimitSides.Ask => "ASK",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static string ToWire(this LunoSides side)
		=> side switch
		{
			LunoSides.Buy => "BUY",
			LunoSides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static string ToWire(this LunoTimeInForce timeInForce)
		=> timeInForce switch
		{
			LunoTimeInForce.GoodTillCancelled => "GTC",
			LunoTimeInForce.ImmediateOrCancel => "IOC",
			LunoTimeInForce.FillOrKill => "FOK",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	public static string ToWire(this LunoStopDirections direction)
		=> direction switch
		{
			LunoStopDirections.Above => "ABOVE",
			LunoStopDirections.Below => "BELOW",
			LunoStopDirections.RelativeLastTrade => "RELATIVE_LAST_TRADE",
			_ => throw new ArgumentOutOfRangeException(nameof(direction),
				direction, null),
		};

	public static DateTime ToLunoTime(this long value, DateTime fallback)
	{
		try
		{
			var timestamp = Math.Abs(value) < 100000000000L
				? DateTimeOffset.FromUnixTimeSeconds(value)
				: DateTimeOffset.FromUnixTimeMilliseconds(value);
			return timestamp.UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return fallback.Kind == DateTimeKind.Utc
				? fallback
				: fallback.ToUniversalTime();
		}
	}

	public static DateTime Align(this DateTime time, TimeSpan interval)
	{
		time = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
		var ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
		return new DateTime(DateTime.UnixEpoch.Ticks +
			(ticks / interval.Ticks * interval.Ticks), DateTimeKind.Utc);
	}
}
