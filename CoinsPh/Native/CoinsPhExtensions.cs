namespace StockSharp.CoinsPh.Native;

static class CoinsPhExtensions
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
		[TimeSpan.FromDays(3)] = "3d",
		[TimeSpan.FromDays(7)] = "1w",
		[TimeSpan.FromDays(30)] = "1M",
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string NormalizeSymbol(this string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.Replace("/", string.Empty).Replace("-", string.Empty)
			.Replace("_", string.Empty).ToUpperInvariant();

	public static string ToWireSymbol(this string symbol)
		=> symbol.NormalizeSymbol().ToLowerInvariant();

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.CoinsPh,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string ToCoinsPhInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Coins.ph does not support this candle time frame.");

	public static TimeSpan ToTimeFrame(this string interval)
		=> _timeFrames.FirstOrDefault(pair => pair.Value.Equals(interval,
			StringComparison.Ordinal)).Key is { } value && value > TimeSpan.Zero
				? value
				: throw new FormatException(
					$"Unsupported Coins.ph candle interval '{interval}'.");

	public static CoinsPhSides ToCoinsPh(this Sides side)
		=> side == Sides.Buy ? CoinsPhSides.Buy : CoinsPhSides.Sell;

	public static Sides ToStockSharp(this CoinsPhSides side)
		=> side == CoinsPhSides.Buy ? Sides.Buy : Sides.Sell;

	public static CoinsPhTimeInForces ToCoinsPh(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue => CoinsPhTimeInForces.GoodTillCanceled,
			TimeInForce.CancelBalance => CoinsPhTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => CoinsPhTimeInForces.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToStockSharp(this CoinsPhTimeInForces timeInForce)
		=> timeInForce switch
		{
			CoinsPhTimeInForces.GoodTillCanceled => TimeInForce.PutInQueue,
			CoinsPhTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			CoinsPhTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderTypes ToStockSharp(this CoinsPhOrderTypes type)
		=> type switch
		{
			CoinsPhOrderTypes.Market => OrderTypes.Market,
			CoinsPhOrderTypes.StopLoss or CoinsPhOrderTypes.StopLossLimit or
				CoinsPhOrderTypes.TakeProfit or CoinsPhOrderTypes.TakeProfitLimit
					=> OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this CoinsPhOrderStatuses status)
		=> status switch
		{
			CoinsPhOrderStatuses.New or CoinsPhOrderStatuses.PartiallyFilled
				=> OrderStates.Active,
			CoinsPhOrderStatuses.Filled or CoinsPhOrderStatuses.PartiallyCanceled or
				CoinsPhOrderStatuses.Canceled or CoinsPhOrderStatuses.Expired
					=> OrderStates.Done,
			CoinsPhOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static string CreateClientId(long transactionId, string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
				>= '0' and <= '9' or '_' or '-').ToArray());
		if (value.IsEmpty())
			value = $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 36 ? value : value[..36];
	}

	public static long ParseTransactionId(string clientId)
		=> clientId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;

	public static DateTime FromMilliseconds(this long timestamp,
		DateTime fallback)
		=> timestamp > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
			: fallback;

	public static long ToMilliseconds(this DateTime value)
	{
		var utc = value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value,
				DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};
		return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
	}
}
