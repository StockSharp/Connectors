namespace StockSharp.HashKey.Native;

static class HashKeyExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames = new()
	{
		[TimeSpan.FromMinutes(1)] = "1m",
		[TimeSpan.FromMinutes(3)] = "3m",
		[TimeSpan.FromMinutes(5)] = "5m",
		[TimeSpan.FromMinutes(15)] = "15m",
		[TimeSpan.FromMinutes(30)] = "30m",
		[TimeSpan.FromHours(1)] = "1h",
		[TimeSpan.FromHours(2)] = "2h",
		[TimeSpan.FromHours(4)] = "4h",
		[TimeSpan.FromHours(6)] = "6h",
		[TimeSpan.FromHours(8)] = "8h",
		[TimeSpan.FromHours(12)] = "12h",
		[TimeSpan.FromDays(1)] = "1d",
		[TimeSpan.FromDays(7)] = "1w",
		[TimeSpan.FromDays(30)] = "1M",
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToHashKey(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"HashKey Global does not document this candle interval.");

	public static string ToBoardCode(this HashKeySections section)
		=> section == HashKeySections.Spot ? BoardCodes.HashKey : BoardCodes.HashKeyFutures;

	public static HashKeySections ToSection(this string boardCode)
	{
		if (boardCode.EqualsIgnoreCase(BoardCodes.HashKey))
			return HashKeySections.Spot;
		if (boardCode.EqualsIgnoreCase(BoardCodes.HashKeyFutures))
			return HashKeySections.Futures;
		throw new ArgumentOutOfRangeException(nameof(boardCode), boardCode,
			"Unknown HashKey Global board code.");
	}

	public static SecurityId ToStockSharp(this string symbol, HashKeySections section)
		=> new()
		{
			SecurityCode = symbol?.Trim().ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static string ToWire(this HashKeyInstrumentTypes type)
		=> type switch
		{
			HashKeyInstrumentTypes.Spot => "SPOT",
			HashKeyInstrumentTypes.Futures => "FUTURES",
			HashKeyInstrumentTypes.Any => "ANY",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static string ToWire(this HashKeyOrderSides side)
		=> side switch
		{
			HashKeyOrderSides.Buy => "BUY",
			HashKeyOrderSides.Sell => "SELL",
			HashKeyOrderSides.BuyOpen => "BUY_OPEN",
			HashKeyOrderSides.SellOpen => "SELL_OPEN",
			HashKeyOrderSides.BuyClose => "BUY_CLOSE",
			HashKeyOrderSides.SellClose => "SELL_CLOSE",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static string ToWire(this HashKeyOrderTypes type)
		=> type switch
		{
			HashKeyOrderTypes.Limit => "LIMIT",
			HashKeyOrderTypes.Market => "MARKET",
			HashKeyOrderTypes.LimitMaker => "LIMIT_MAKER",
			HashKeyOrderTypes.MarketOfBase => "MARKET_OF_BASE",
			HashKeyOrderTypes.MarketOfQuote => "MARKET_OF_QUOTE",
			HashKeyOrderTypes.Stop => "STOP",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static string ToWire(this HashKeyTimeInForces value)
		=> value switch
		{
			HashKeyTimeInForces.GoodTillCanceled => "GTC",
			HashKeyTimeInForces.ImmediateOrCancel => "IOC",
			HashKeyTimeInForces.FillOrKill => "FOK",
			HashKeyTimeInForces.LimitMaker => "LIMIT_MAKER",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWire(this HashKeyPriceTypes value)
		=> value switch
		{
			HashKeyPriceTypes.Input => "INPUT",
			HashKeyPriceTypes.Market => "MARKET",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWire(this HashKeySelfTradePreventionModes value)
		=> value switch
		{
			HashKeySelfTradePreventionModes.ExpireTaker => "EXPIRE_TAKER",
			HashKeySelfTradePreventionModes.ExpireMaker => "EXPIRE_MAKER",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWire(this HashKeyPositionSides value)
		=> value switch
		{
			HashKeyPositionSides.Long => "LONG",
			HashKeyPositionSides.Short => "SHORT",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static HashKeyOrderSides ToHashKey(this Sides side,
		HashKeySections section, bool isClose)
		=> section == HashKeySections.Spot
			? side == Sides.Buy ? HashKeyOrderSides.Buy : HashKeyOrderSides.Sell
			: (side, isClose) switch
			{
				(Sides.Buy, false) => HashKeyOrderSides.BuyOpen,
				(Sides.Sell, false) => HashKeyOrderSides.SellOpen,
				(Sides.Buy, true) => HashKeyOrderSides.BuyClose,
				(Sides.Sell, true) => HashKeyOrderSides.SellClose,
				_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
			};

	public static Sides ToStockSharp(this HashKeyOrderSides side)
		=> side is HashKeyOrderSides.Buy or HashKeyOrderSides.BuyOpen or
			HashKeyOrderSides.BuyClose ? Sides.Buy : Sides.Sell;

	public static OrderPositionEffects? ToPositionEffect(this HashKeyOrderSides side)
		=> side is HashKeyOrderSides.BuyClose or HashKeyOrderSides.SellClose
			? OrderPositionEffects.CloseOnly
			: side is HashKeyOrderSides.BuyOpen or HashKeyOrderSides.SellOpen
				? OrderPositionEffects.OpenOnly
				: null;

	public static HashKeyTimeInForces ToHashKey(this TimeInForce? timeInForce,
		bool isPostOnly)
		=> isPostOnly
			? HashKeyTimeInForces.LimitMaker
			: timeInForce switch
			{
				TimeInForce.CancelBalance => HashKeyTimeInForces.ImmediateOrCancel,
				TimeInForce.MatchOrCancel => HashKeyTimeInForces.FillOrKill,
				_ => HashKeyTimeInForces.GoodTillCanceled,
			};

	public static TimeInForce? ToStockSharp(this HashKeyTimeInForces? timeInForce)
		=> timeInForce switch
		{
			HashKeyTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			HashKeyTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderTypes ToStockSharp(this HashKeyOrderTypes type)
		=> type switch
		{
			HashKeyOrderTypes.Market or HashKeyOrderTypes.MarketOfBase or
				HashKeyOrderTypes.MarketOfQuote => OrderTypes.Market,
			HashKeyOrderTypes.Stop => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this HashKeyOrderStatuses status)
		=> status switch
		{
			HashKeyOrderStatuses.New or HashKeyOrderStatuses.PartiallyFilled or
				HashKeyOrderStatuses.PendingCancel or HashKeyOrderStatuses.StopNew =>
				OrderStates.Active,
			HashKeyOrderStatuses.Filled or HashKeyOrderStatuses.Canceled or
				HashKeyOrderStatuses.PartiallyCanceled or HashKeyOrderStatuses.StopFilled or
				HashKeyOrderStatuses.StopCanceled or HashKeyOrderStatuses.StopNotEffective =>
				OrderStates.Done,
			HashKeyOrderStatuses.Rejected or HashKeyOrderStatuses.StopRejected or
				HashKeyOrderStatuses.StopFailed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static SecurityStates ToStockSharp(this HashKeyTradingStatuses status)
		=> status is HashKeyTradingStatuses.Trading or HashKeyTradingStatuses.Resuming
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static decimal? ToNullableDecimal(this string value)
		=> value.IsEmpty()
			? null
			: decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
				out var result)
				? result
				: throw new FormatException($"HashKey decimal '{value}' is invalid.");

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static long ToMilliseconds(this DateTime value)
	{
		var utc = value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};
		return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
	}

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime FromHashKeyMilliseconds(this long timestamp,
		DateTime fallback)
		=> timestamp > 0 ? timestamp.FromMilliseconds() : fallback;

	public static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		return value.Length <= 255 ? value : value[..255];
	}

	public static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}
