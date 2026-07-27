namespace StockSharp.Bit2Me.Native;

static class Bit2MeExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static string NormalizeSymbol(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant()
			.Replace('_', '/').Replace('-', '/');
		var parts = symbol.Split('/', StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Bit2Me symbol '{symbol}' is not in BASE/QUOTE format.");
		return $"{parts[0]}/{parts[1]}";
	}

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeSymbol(),
			BoardCode = BoardCodes.Bit2Me,
		};

	public static (string Base, string Quote) SplitSymbol(this string symbol)
	{
		var parts = symbol.NormalizeSymbol().Split('/');
		return (parts[0], parts[1]);
	}

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

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

	public static string ToBit2MeDate(this DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	public static DateTime ToUtcDateTime(this string value, DateTime fallback)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
			out var timestamp)
			? timestamp.UtcDateTime
			: fallback;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static int ToBit2MeInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? 1
			: timeFrame == TimeSpan.FromMinutes(5) ? 5
			: timeFrame == TimeSpan.FromMinutes(15) ? 15
			: timeFrame == TimeSpan.FromMinutes(30) ? 30
			: timeFrame == TimeSpan.FromHours(1) ? 60
			: timeFrame == TimeSpan.FromHours(4) ? 240
			: timeFrame == TimeSpan.FromDays(1) ? 1440
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Bit2Me candle interval.");

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var i = 0; i < precision; i++)
			step /= 10m;
		return step;
	}

	public static Sides ToStockSharp(this Bit2MeSides side)
		=> side == Bit2MeSides.Buy ? Sides.Buy : Sides.Sell;

	public static Bit2MeSides ToBit2Me(this Sides side)
		=> side == Sides.Buy ? Bit2MeSides.Buy : Bit2MeSides.Sell;

	public static string ToNative(this Bit2MeOrderTypes orderType)
		=> orderType switch
		{
			Bit2MeOrderTypes.StopLimit => "stop-limit",
			Bit2MeOrderTypes.Market => "market",
			_ => "limit",
		};

	public static OrderTypes ToStockSharp(this Bit2MeOrderTypes orderType)
		=> orderType switch
		{
			Bit2MeOrderTypes.Market => OrderTypes.Market,
			Bit2MeOrderTypes.StopLimit => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this Bit2MeOrderStatuses status)
		=> status switch
		{
			Bit2MeOrderStatuses.Open or Bit2MeOrderStatuses.Inactive =>
				OrderStates.Active,
			Bit2MeOrderStatuses.Filled or Bit2MeOrderStatuses.Cancelled =>
				OrderStates.Done,
			_ => OrderStates.None,
		};

	public static Bit2MeTimeInForces ToBit2Me(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => Bit2MeTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => Bit2MeTimeInForces.FillOrKill,
			_ => Bit2MeTimeInForces.GoodTillCancelled,
		};

	public static TimeInForce? ToStockSharp(this Bit2MeTimeInForces? timeInForce)
		=> timeInForce switch
		{
			Bit2MeTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			Bit2MeTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static SecurityStates ToStockSharp(this Bit2MeMarketStatuses status)
		=> status is Bit2MeMarketStatuses.Enabled or Bit2MeMarketStatuses.EnabledAt
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientOrderId(long transactionId, string userOrderId)
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
