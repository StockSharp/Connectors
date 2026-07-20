namespace StockSharp.Extended.Native;

static class ExtendedExtensions
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
		TimeSpan.FromDays(1),
	];

	public static string ToExtendedInterval(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => "PT1M",
			{ TotalMinutes: 5 } => "PT5M",
			{ TotalMinutes: 15 } => "PT15M",
			{ TotalMinutes: 30 } => "PT30M",
			{ TotalHours: 1 } => "PT1H",
			{ TotalHours: 2 } => "PT2H",
			{ TotalHours: 4 } => "PT4H",
			{ TotalDays: 1 } => "P1D",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Extended candle time frame."),
		};

	public static decimal ParseExtendedDecimal(this string value,
		string fieldName)
	{
		if (value.IsEmpty() || !decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				"Extended returned invalid " + fieldName + " '" + value + "'.");
		return result;
	}

	public static decimal? TryParseExtendedDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToExtendedWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToExtendedTime(this long milliseconds,
		string fieldName = "timestamp")
	{
		if (milliseconds <= 0)
			throw new InvalidDataException(
				"Extended returned invalid " + fieldName + " " + milliseconds + ".");
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(milliseconds);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Extended " + fieldName + " is outside the supported range.", error);
		}
	}

	public static DateTime ToExtendedTimeOrNow(this long milliseconds)
		=> milliseconds > 0 ? milliseconds.ToExtendedTime() : DateTime.UtcNow;

	public static DateTime EnsureExtendedUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static long ToExtendedUnixMilliseconds(this DateTime value)
		=> checked((long)Math.Ceiling(
			(value.EnsureExtendedUtc() - DateTime.UnixEpoch).TotalMilliseconds));

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim(),
			BoardCode = BoardCodes.Extended,
		};

	public static Sides ToStockSharp(this ExtendedSides side)
		=> side switch
		{
			ExtendedSides.Buy => Sides.Buy,
			ExtendedSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported Extended side."),
		};

	public static ExtendedSides ToExtended(this Sides side)
		=> side switch
		{
			Sides.Buy => ExtendedSides.Buy,
			Sides.Sell => ExtendedSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported order side."),
		};

	public static Sides ToStockSharp(this ExtendedPositionSides side)
		=> side switch
		{
			ExtendedPositionSides.Long => Sides.Buy,
			ExtendedPositionSides.Short => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported Extended position side."),
		};

	public static ExtendedTimeInForces ToExtended(this TimeInForce? timeInForce,
		bool isPostOnly, OrderTypes orderType)
	{
		if (isPostOnly)
		{
			if (orderType != OrderTypes.Limit ||
				timeInForce is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Extended post-only orders must be limit GTT orders.");
			return ExtendedTimeInForces.GoodTillTime;
		}
		return timeInForce switch
		{
			TimeInForce.CancelBalance => ExtendedTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => throw new NotSupportedException(
				"Extended deprecated fill-or-kill orders."),
			TimeInForce.PutInQueue or null when orderType == OrderTypes.Market =>
				ExtendedTimeInForces.ImmediateOrCancel,
			TimeInForce.PutInQueue or null => ExtendedTimeInForces.GoodTillTime,
			_ => throw new NotSupportedException(
				"Extended does not support time in force '" + timeInForce + "'."),
		};
	}

	public static TimeInForce ToStockSharp(this ExtendedTimeInForces timeInForce)
		=> timeInForce switch
		{
			ExtendedTimeInForces.GoodTillTime => TimeInForce.PutInQueue,
			ExtendedTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			ExtendedTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce,
				"Unsupported Extended time in force."),
		};

	public static ExtendedOrderTypes ToExtended(this OrderTypes orderType)
		=> orderType switch
		{
			OrderTypes.Limit => ExtendedOrderTypes.Limit,
			OrderTypes.Market => ExtendedOrderTypes.Market,
			OrderTypes.Conditional => ExtendedOrderTypes.Conditional,
			_ => throw new NotSupportedException(
				"Extended does not support order type '" + orderType + "'."),
		};

	public static OrderTypes ToStockSharp(this ExtendedOrderTypes orderType)
		=> orderType switch
		{
			ExtendedOrderTypes.Limit => OrderTypes.Limit,
			ExtendedOrderTypes.Market => OrderTypes.Market,
			ExtendedOrderTypes.Conditional or
			ExtendedOrderTypes.TakeProfitStopLoss => OrderTypes.Conditional,
			ExtendedOrderTypes.Twap => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Unsupported Extended order type."),
		};

	public static OrderStates ToStockSharp(this ExtendedOrderStatuses status)
		=> status switch
		{
			ExtendedOrderStatuses.New or ExtendedOrderStatuses.Untriggered or
			ExtendedOrderStatuses.PartiallyFilled => OrderStates.Active,
			ExtendedOrderStatuses.Filled or ExtendedOrderStatuses.Cancelled or
			ExtendedOrderStatuses.Expired => OrderStates.Done,
			ExtendedOrderStatuses.Rejected => OrderStates.Failed,
			ExtendedOrderStatuses.Unknown => OrderStates.Pending,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unsupported Extended order status."),
		};

	public static SecurityTypes ToStockSharp(this ExtendedMarketTypes marketType)
		=> marketType switch
		{
			ExtendedMarketTypes.Perpetual => SecurityTypes.Future,
			ExtendedMarketTypes.Spot => SecurityTypes.CryptoCurrency,
			_ => throw new ArgumentOutOfRangeException(nameof(marketType), marketType,
				"Unsupported Extended market type."),
		};

	public static string ToWire(this ExtendedStreamScopes scope)
		=> scope switch
		{
			ExtendedStreamScopes.OrderBooks => "orderbooks",
			ExtendedStreamScopes.Trades => "trades",
			ExtendedStreamScopes.FundingRates => "funding-rates",
			ExtendedStreamScopes.Prices => "prices",
			ExtendedStreamScopes.Candles => "candles",
			ExtendedStreamScopes.Account => "account",
			_ => throw new ArgumentOutOfRangeException(nameof(scope), scope,
				"Unsupported Extended stream scope."),
		};

	public static string ToWire(this ExtendedCandleTypes candleType)
		=> candleType switch
		{
			ExtendedCandleTypes.Last => "last",
			ExtendedCandleTypes.Mark => "mark",
			ExtendedCandleTypes.Index => "index",
			_ => throw new ArgumentOutOfRangeException(nameof(candleType), candleType,
				"Unsupported Extended candle type."),
		};

	public static string ToWire(this ExtendedPriceTypes priceType)
		=> priceType switch
		{
			ExtendedPriceTypes.Mark => "mark",
			ExtendedPriceTypes.Index => "index",
			_ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType,
				"Unsupported Extended price type."),
		};

	public static ExtendedRpcParameters ToParameters(
		this ExtendedSubscriptionKey key, string apiKey)
	{
		var selector = new ExtendedRpcSelector
		{
			Market = key.Market,
		};
		switch (key.Scope)
		{
			case ExtendedStreamScopes.OrderBooks:
				selector.Depth = key.Detail.ThrowIfEmpty(nameof(key.Detail));
				selector.IsRequestForQuoteOnly = false;
				break;
			case ExtendedStreamScopes.Trades:
			case ExtendedStreamScopes.FundingRates:
				break;
			case ExtendedStreamScopes.Prices:
				selector.Type = key.Detail.ThrowIfEmpty(nameof(key.Detail));
				break;
			case ExtendedStreamScopes.Candles:
				selector.Type = key.Detail.ThrowIfEmpty(nameof(key.Detail));
				selector.Interval = key.Interval.ThrowIfEmpty(nameof(key.Interval));
				break;
			case ExtendedStreamScopes.Account:
				selector.Market = null;
				selector.Account = key.Account.ThrowIfEmpty(nameof(key.Account));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(key), key,
					"Unsupported Extended stream scope.");
		}
		return new()
		{
			Scope = key.Scope.ToWire(),
			Selector = selector,
			ApiKey = key.Scope == ExtendedStreamScopes.Account
				? apiKey.ThrowIfEmpty(nameof(apiKey))
				: null,
		};
	}

	public static bool IsMultipleOf(this decimal value, decimal step)
		=> step > 0 && value % step == 0;
}
