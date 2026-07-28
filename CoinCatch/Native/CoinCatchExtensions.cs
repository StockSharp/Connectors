namespace StockSharp.CoinCatch.Native;

static class CoinCatchExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(3), "3day" },
		{ TimeSpan.FromDays(7), "1week" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	private static readonly PairSet<TimeSpan, string>
		_webSocketTimeFrames = new()
		{
			{ TimeSpan.FromMinutes(1), "1m" },
			{ TimeSpan.FromMinutes(5), "5m" },
			{ TimeSpan.FromMinutes(15), "15m" },
			{ TimeSpan.FromMinutes(30), "30m" },
			{ TimeSpan.FromHours(1), "1H" },
			{ TimeSpan.FromHours(4), "4H" },
			{ TimeSpan.FromHours(6), "6H" },
			{ TimeSpan.FromHours(12), "12H" },
			{ TimeSpan.FromDays(1), "1D" },
			{ TimeSpan.FromDays(3), "3D" },
			{ TimeSpan.FromDays(7), "1W" },
			{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
		};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static bool IsFutures(this CoinCatchProductTypes productType)
		=> productType != CoinCatchProductTypes.Spot;

	public static string ToProductCode(
		this CoinCatchProductTypes productType)
		=> productType switch
		{
			CoinCatchProductTypes.Spot => "spbl",
			CoinCatchProductTypes.UsdtFutures => "umcbl",
			CoinCatchProductTypes.CoinFutures => "dmcbl",
			_ => throw new ArgumentOutOfRangeException(
				nameof(productType), productType,
				LocalizedStrings.InvalidValue),
		};

	public static string ToBoardCode(
		this CoinCatchProductTypes productType)
		=> productType switch
		{
			CoinCatchProductTypes.Spot => BoardCodes.CoinCatch,
			CoinCatchProductTypes.UsdtFutures =>
				BoardCodes.CoinCatchFutUsdt,
			CoinCatchProductTypes.CoinFutures =>
				BoardCodes.CoinCatchFutCoin,
			_ => throw new ArgumentOutOfRangeException(
				nameof(productType), productType,
				LocalizedStrings.InvalidValue),
		};

	public static string ToWebSocketInstrumentType(
		this CoinCatchProductTypes productType)
		=> productType.IsFutures() ? "MC" : "SP";

	public static string ToCoinCatchGranularity(
		this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported CoinCatch candle interval.");

	public static string ToCoinCatchWebSocketChannel(
		this TimeSpan timeFrame)
		=> "candle" + (_webSocketTimeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported CoinCatch candle interval."));

	public static TimeSpan ToCoinCatchTimeFrame(this string channel)
	{
		channel = channel.ThrowIfEmpty(nameof(channel)).Trim();
		if (channel.StartsWith("candle",
			StringComparison.OrdinalIgnoreCase))
			channel = channel["candle".Length..];
		return _webSocketTimeFrames.TryGetKey2(channel) ??
			throw new ArgumentOutOfRangeException(
				nameof(channel), channel,
				"Unsupported CoinCatch candle interval.");
	}

	public static string ToCoinCatchWebSocketSymbol(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.ToUpperInvariant();
		var separator = symbol.LastIndexOf('_');
		return separator > 0 ? symbol[..separator] : symbol;
	}

	public static string CreateSecurityCode(string baseCoin,
		string quoteCoin)
		=> $"{baseCoin.ThrowIfEmpty(nameof(baseCoin)).Trim().ToUpperInvariant()}/" +
			quoteCoin.ThrowIfEmpty(nameof(quoteCoin)).Trim()
				.ToUpperInvariant();

	public static SecurityId ToStockSharp(this CoinCatchSymbol symbol,
		CoinCatchProductTypes productType)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = productType.ToBoardCode(),
		};

	public static DateTime FromCoinCatchTime(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static long ToCoinCatchTime(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(
				value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static decimal? GetStep(int? precision)
	{
		if (precision is null or < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < precision; index++)
			step /= 10m;
		return step;
	}

	public static string ToCoinCatch(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToCoinCatchFutures(
		this Sides side, bool reduceOnly)
		=> (side, reduceOnly) switch
		{
			(Sides.Buy, false) => "open_long",
			(Sides.Sell, false) => "open_short",
			(Sides.Buy, true) => "close_short",
			(Sides.Sell, true) => "close_long",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" or "open_long" or "close_short" or "buy_single" =>
				Sides.Buy,
			"sell" or "open_short" or "close_long" or "sell_single" =>
				Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToCoinCatch(this OrderTypes? orderType)
		=> orderType switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(
				nameof(orderType), orderType,
				LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this CoinCatchOrder order)
		=> order?.OrderType?.ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static string ToCoinCatch(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => "normal",
		};

	public static TimeInForce ToTimeInForce(this string timeInForce)
		=> timeInForce?.Trim().ToLowerInvariant() switch
		{
			"fok" => TimeInForce.MatchOrCancel,
			"ioc" => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.Trim().ToLowerInvariant() switch
		{
			"new" or "live" or "partial_fill" or
				"partially_filled" => OrderStates.Active,
			"full_fill" or "filled" or "cancelled" or "canceled" or
				"expired" => OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static SecurityStates ToSecurityState(this string status)
		=> status?.Trim().ToLowerInvariant() is "online" or "normal"
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientOrderId(
		long transactionId, string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			char.IsAsciiLetterOrDigit(character) ||
			character is '_' or '-').ToArray());
		if (value.IsEmpty())
			value =
				$"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 64 ? value : value[..64];
	}

	public static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith(
			"ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}
