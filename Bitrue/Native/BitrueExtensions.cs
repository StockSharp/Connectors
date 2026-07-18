namespace StockSharp.Bitrue.Native;

static class BitrueExtensions
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
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static BitrueSpotCandleIntervals ToBitrueSpotInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? BitrueSpotCandleIntervals.Minute1
		: timeFrame == TimeSpan.FromMinutes(5) ? BitrueSpotCandleIntervals.Minute5
		: timeFrame == TimeSpan.FromMinutes(15) ? BitrueSpotCandleIntervals.Minute15
		: timeFrame == TimeSpan.FromMinutes(30) ? BitrueSpotCandleIntervals.Minute30
		: timeFrame == TimeSpan.FromHours(1) ? BitrueSpotCandleIntervals.Hour1
		: timeFrame == TimeSpan.FromHours(2) ? BitrueSpotCandleIntervals.Hour2
		: timeFrame == TimeSpan.FromHours(4) ? BitrueSpotCandleIntervals.Hour4
		: timeFrame == TimeSpan.FromHours(12) ? BitrueSpotCandleIntervals.Hour12
		: timeFrame == TimeSpan.FromDays(1) ? BitrueSpotCandleIntervals.Day1
		: timeFrame == TimeSpan.FromDays(7) ? BitrueSpotCandleIntervals.Week1
		: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Bitrue spot does not support this candle time-frame.");

	public static BitrueFuturesCandleIntervals ToBitrueFuturesInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? BitrueFuturesCandleIntervals.Minute1
		: timeFrame == TimeSpan.FromMinutes(5) ? BitrueFuturesCandleIntervals.Minute5
		: timeFrame == TimeSpan.FromMinutes(15) ? BitrueFuturesCandleIntervals.Minute15
		: timeFrame == TimeSpan.FromMinutes(30) ? BitrueFuturesCandleIntervals.Minute30
		: timeFrame == TimeSpan.FromHours(1) ? BitrueFuturesCandleIntervals.Hour1
		: timeFrame == TimeSpan.FromHours(2) ? BitrueFuturesCandleIntervals.Hour2
		: timeFrame == TimeSpan.FromHours(4) ? BitrueFuturesCandleIntervals.Hour4
		: timeFrame == TimeSpan.FromDays(1) ? BitrueFuturesCandleIntervals.Day1
		: timeFrame == TimeSpan.FromDays(7) ? BitrueFuturesCandleIntervals.Week1
		: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Bitrue futures does not support this candle time-frame.");

	public static string ToWire(this BitrueSpotCandleIntervals value)
		=> value switch
		{
			BitrueSpotCandleIntervals.Minute1 => "1m",
			BitrueSpotCandleIntervals.Minute5 => "5m",
			BitrueSpotCandleIntervals.Minute15 => "15m",
			BitrueSpotCandleIntervals.Minute30 => "30m",
			BitrueSpotCandleIntervals.Hour1 => "1H",
			BitrueSpotCandleIntervals.Hour2 => "2H",
			BitrueSpotCandleIntervals.Hour4 => "4H",
			BitrueSpotCandleIntervals.Hour12 => "12H",
			BitrueSpotCandleIntervals.Day1 => "1D",
			BitrueSpotCandleIntervals.Week1 => "1W",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToRestWire(this BitrueFuturesCandleIntervals value)
		=> value switch
		{
			BitrueFuturesCandleIntervals.Minute1 => "1min",
			BitrueFuturesCandleIntervals.Minute5 => "5min",
			BitrueFuturesCandleIntervals.Minute15 => "15min",
			BitrueFuturesCandleIntervals.Minute30 => "30min",
			BitrueFuturesCandleIntervals.Hour1 => "1h",
			BitrueFuturesCandleIntervals.Hour2 => "2h",
			BitrueFuturesCandleIntervals.Hour4 => "4h",
			BitrueFuturesCandleIntervals.Day1 => "1day",
			BitrueFuturesCandleIntervals.Week1 => "1week",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWsWire(this BitrueFuturesCandleIntervals value)
		=> value switch
		{
			BitrueFuturesCandleIntervals.Minute1 => "1min",
			BitrueFuturesCandleIntervals.Minute5 => "5min",
			BitrueFuturesCandleIntervals.Minute15 => "15min",
			BitrueFuturesCandleIntervals.Minute30 => "30min",
			BitrueFuturesCandleIntervals.Hour1 => "60min",
			BitrueFuturesCandleIntervals.Hour2 => "2h",
			BitrueFuturesCandleIntervals.Hour4 => "4h",
			BitrueFuturesCandleIntervals.Day1 => "1day",
			BitrueFuturesCandleIntervals.Week1 => "1week",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToPublicWsSymbol(this string symbol, BitrueSections section)
		=> section == BitrueSections.Spot
			? symbol.ThrowIfEmpty(nameof(symbol)).ToLowerInvariant()
			: symbol.ThrowIfEmpty(nameof(symbol)).Replace('-', '_').ToLowerInvariant();

	public static string ToPrivateWsSymbol(this string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.Replace("E-", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();

	public static SecurityId ToStockSharp(this string symbol, BitrueSections section)
		=> new()
		{
			SecurityCode = symbol?.ToUpperInvariant(),
			BoardCode = section == BitrueSections.Spot
				? BoardCodes.Bitrue
				: BoardCodes.BitrueFutures,
		};

	public static Sides ToStockSharp(this BitrueSides side)
		=> side == BitrueSides.Sell ? Sides.Sell : Sides.Buy;

	public static Sides ToStockSharp(this BitrueSpotWsSides side)
		=> side == BitrueSpotWsSides.Sell ? Sides.Sell : Sides.Buy;

	public static OrderTypes ToStockSharp(this BitrueOrderTypes type)
		=> type == BitrueOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderTypes ToStockSharp(this BitrueSpotWsOrderTypes type)
		=> type == BitrueSpotWsOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToStockSharp(this BitrueSpotOrderStatuses state)
		=> state switch
		{
			BitrueSpotOrderStatuses.PendingCreate or BitrueSpotOrderStatuses.New or
				BitrueSpotOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitrueSpotOrderStatuses.Filled or BitrueSpotOrderStatuses.Canceled or
				BitrueSpotOrderStatuses.Expired => OrderStates.Done,
			BitrueSpotOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToStockSharp(this BitrueSpotWsOrderStatuses state)
		=> state switch
		{
			BitrueSpotWsOrderStatuses.Pending or BitrueSpotWsOrderStatuses.Active or
				BitrueSpotWsOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitrueSpotWsOrderStatuses.Done or BitrueSpotWsOrderStatuses.Canceled =>
				OrderStates.Done,
			_ => OrderStates.None,
		};

	public static OrderStates ToStockSharp(this BitrueFuturesOrderStatuses state)
		=> state switch
		{
			BitrueFuturesOrderStatuses.Init or BitrueFuturesOrderStatuses.New or
				BitrueFuturesOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitrueFuturesOrderStatuses.Filled or BitrueFuturesOrderStatuses.Canceled or
				BitrueFuturesOrderStatuses.Cancelled => OrderStates.Done,
			BitrueFuturesOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static BitrueOrderTypes ToBitrue(this OrderTypes type, bool isPostOnly,
		TimeInForce? timeInForce)
	{
		if (type == OrderTypes.Market)
			return BitrueOrderTypes.Market;
		if (isPostOnly)
			return BitrueOrderTypes.PostOnly;
		return timeInForce switch
		{
			TimeInForce.CancelBalance => BitrueOrderTypes.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BitrueOrderTypes.FillOrKill,
			_ => BitrueOrderTypes.Limit,
		};
	}

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToUtcTime(this long timestamp)
		=> Math.Abs(timestamp) < 100_000_000_000L
			? DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
			: DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static decimal PriceStepFromPrecision(this int precision)
	{
		if (precision is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(precision), precision, null);
		var step = 1m;
		for (var i = 0; i < precision; i++)
			step /= 10m;
		return step;
	}

	public static int UnGzipTo(this ReadOnlyMemory<byte> source, Memory<byte> destination)
	{
		using var input = new MemoryStream(source.ToArray(), false);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		var written = 0;
		while (written < destination.Length)
		{
			var count = gzip.Read(destination.Span[written..]);
			if (count == 0)
				return written;
			written += count;
		}
		if (gzip.ReadByte() >= 0)
			throw new InvalidDataException(
				"The Bitrue WebSocket decompression buffer is too small.");
		return written;
	}
}
