namespace StockSharp.CoinTR.Native;

static class CoinTRExtensions
{
	private static readonly PairSet<TimeSpan, string>
		_restTimeFrames = new()
		{
			{ TimeSpan.FromMinutes(1), "1min" },
			{ TimeSpan.FromMinutes(3), "3min" },
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
			{ TimeSpan.FromMinutes(3), "3m" },
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

	public static IEnumerable<TimeSpan> TimeFrames
		=> _restTimeFrames.Keys;

	public static string ToCoinTRGranularity(this TimeSpan timeFrame)
		=> _restTimeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported CoinTR candle interval.");

	public static string ToCoinTRWebSocketInterval(
		this TimeSpan timeFrame)
		=> _webSocketTimeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported CoinTR candle interval.");

	public static TimeSpan ToCoinTRTimeFrame(this string interval)
		=> _webSocketTimeFrames.TryGetKey2(
			interval.ThrowIfEmpty(nameof(interval))) ??
			throw new ArgumentOutOfRangeException(nameof(interval),
				interval, "Unsupported CoinTR candle interval.");

	public static string CreateSecurityCode(string baseCoin,
		string quoteCoin)
		=> $"{baseCoin.ThrowIfEmpty(nameof(baseCoin)).Trim().ToUpperInvariant()}/" +
			quoteCoin.ThrowIfEmpty(nameof(quoteCoin)).Trim()
				.ToUpperInvariant();

	public static SecurityId ToStockSharp(this CoinTRSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.CoinTR,
		};

	public static DateTime FromCoinTRTime(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static long ToCoinTRTime(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value,
				DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var i = 0; i < precision; i++)
			step /= 10m;
		return step;
	}

	public static string ToCoinTR(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				LocalizedStrings.InvalidValue),
		};

	public static string ToCoinTR(this OrderTypes? orderType)
		=> orderType switch
		{
			null or OrderTypes.Limit or OrderTypes.Conditional => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType),
				orderType, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this CoinTROrder order)
		=> order?.TriggerType.EqualsIgnoreCase("tpsl") == true
			? OrderTypes.Conditional
			: order?.OrderType?.ToLowerInvariant() switch
			{
				"limit" => OrderTypes.Limit,
				"market" => OrderTypes.Market,
				_ => OrderTypes.Limit,
			};

	public static string ToCoinTR(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => "gtc",
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
			"new" or "live" or "partially_filled" => OrderStates.Active,
			"filled" or "cancelled" or "canceled" or "expired" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static SecurityStates ToSecurityState(this string status)
		=> status?.Trim().ToLowerInvariant() == "online"
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
			.ToArray());
		if (value.IsEmpty())
			value = $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 64 ? value : value[..64];
	}

	public static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-",
			StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}
