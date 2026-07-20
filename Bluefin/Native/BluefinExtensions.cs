namespace StockSharp.Bluefin.Native;

static class BluefinExtensions
{
	private const decimal _e9 = 1_000_000_000m;

	public static string ExchangeEndpoint(this BluefinEnvironments environment)
		=> environment switch
		{
			BluefinEnvironments.Mainnet =>
				"https://api.sui-prod.bluefin.io",
			BluefinEnvironments.Testnet =>
				"https://api.sui-staging.bluefin.io",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string TradeEndpoint(this BluefinEnvironments environment)
		=> environment switch
		{
			BluefinEnvironments.Mainnet =>
				"https://trade.api.sui-prod.bluefin.io",
			BluefinEnvironments.Testnet =>
				"https://trade.api.sui-staging.bluefin.io",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string AuthEndpoint(this BluefinEnvironments environment)
		=> environment switch
		{
			BluefinEnvironments.Mainnet =>
				"https://auth.api.sui-prod.bluefin.io",
			BluefinEnvironments.Testnet =>
				"https://auth.api.sui-staging.bluefin.io",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string MarketSocketEndpoint(
		this BluefinEnvironments environment)
		=> environment switch
		{
			BluefinEnvironments.Mainnet =>
				"wss://stream.api.sui-prod.bluefin.io/ws/market",
			BluefinEnvironments.Testnet =>
				"wss://stream.api.sui-staging.bluefin.io/ws/market",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string AccountSocketEndpoint(
		this BluefinEnvironments environment)
		=> environment switch
		{
			BluefinEnvironments.Mainnet =>
				"wss://stream.api.sui-prod.bluefin.io/ws/account",
			BluefinEnvironments.Testnet =>
				"wss://stream.api.sui-staging.bluefin.io/ws/account",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static decimal ParseE9(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed))
			throw new InvalidDataException(
				$"Bluefin returned invalid {field} '{value}'.");
		return parsed / _e9;
	}

	public static decimal? TryParseE9(this string value)
		=> decimal.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed / _e9
			: null;

	public static string ToE9(this decimal value, string field)
	{
		var scaled = value * _e9;
		if (scaled != decimal.Truncate(scaled))
			throw new ArgumentOutOfRangeException(field, value,
				$"Bluefin {field} cannot have more than nine decimals.");
		return scaled.ToString("0", CultureInfo.InvariantCulture);
	}

	public static DateTime FromBluefinMilliseconds(this long value)
	{
		if (value <= 0 || value > 253_402_300_799_999L)
			throw new InvalidDataException(
				$"Bluefin returned invalid timestamp '{value}'.");
		return DateTime.UnixEpoch.AddMilliseconds(value);
	}

	public static long ToBluefinMilliseconds(this DateTime value)
		=> checked((long)(value.ToUniversalTime() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static string NormalizeSuiAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length is < 1 or > 64 ||
			value.Any(static character => !Uri.IsHexDigit(character)))
			throw new FormatException("A Sui address must contain up to 32 " +
				"hexadecimal bytes.");
		return "0x" + value.PadLeft(64, '0').ToLowerInvariant();
	}

	public static Uri NormalizeHttpEndpoint(this string value, string name)
	{
		var endpoint = new Uri(value.ThrowIfEmpty(name).Trim().TrimEnd('/') +
			"/", UriKind.Absolute);
		if (endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException("An HTTP endpoint is required.", name);
		return endpoint;
	}

	public static Uri NormalizeSocketEndpoint(this string value, string name)
	{
		var endpoint = new Uri(value.ThrowIfEmpty(name).Trim(), UriKind.Absolute);
		if (endpoint.Scheme is not ("ws" or "wss"))
			throw new ArgumentException("A WebSocket endpoint is required.", name);
		return endpoint;
	}

	public static SecurityId ToBluefinSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.Bluefin,
		};

	public static SecurityId ToBluefinSecurityId(this BluefinMarket market)
		=> market.Symbol.ToBluefinSecurityId();

	public static Sides ToStockSharpSide(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"LONG" => Sides.Buy,
			"SHORT" => Sides.Sell,
			_ => throw new InvalidDataException(
				$"Bluefin returned unknown side '{value}'."),
		};

	public static string ToBluefinSide(this Sides value)
		=> value switch
		{
			Sides.Buy => "LONG",
			Sides.Sell => "SHORT",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static OrderStates ToStockSharpOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"STANDBY" => OrderStates.Pending,
			"OPEN" or "PARTIALLY_FILLED_OPEN" => OrderStates.Active,
			"FILLED" or "CANCELLED" or "PARTIALLY_FILLED_CANCELED" or
				"EXPIRED" or "PARTIALLY_FILLED_EXPIRED" => OrderStates.Done,
			"UNSPECIFIED" or null or "" => OrderStates.Pending,
			_ => OrderStates.Failed,
		};

	public static OrderTypes ToStockSharpOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"LIMIT" => OrderTypes.Limit,
			"STOP_MARKET" or "STOP_LOSS_MARKET" or
				"TAKE_PROFIT_MARKET" or "STOP_LIMIT" or "STOP_LOSS_LIMIT" or
				"TAKE_PROFIT_LIMIT" => OrderTypes.Conditional,
			_ => OrderTypes.Conditional,
		};

	public static string ToBluefinTimeInForce(this TimeInForce? value,
		OrderTypes orderType)
		=> orderType == OrderTypes.Market ? null : value switch
		{
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => "GTT",
		};

	public static string ToBluefinInterval(this TimeSpan value)
		=> value == TimeSpan.FromMinutes(1) ? "1m" :
			value == TimeSpan.FromMinutes(3) ? "3m" :
			value == TimeSpan.FromMinutes(5) ? "5m" :
			value == TimeSpan.FromMinutes(15) ? "15m" :
			value == TimeSpan.FromMinutes(30) ? "30m" :
			value == TimeSpan.FromHours(1) ? "1h" :
			value == TimeSpan.FromHours(2) ? "2h" :
			value == TimeSpan.FromHours(4) ? "4h" :
			value == TimeSpan.FromHours(6) ? "6h" :
			value == TimeSpan.FromHours(8) ? "8h" :
			value == TimeSpan.FromHours(12) ? "12h" :
			value == TimeSpan.FromDays(1) ? "1d" :
			value == TimeSpan.FromDays(7) ? "1w" :
			value == TimeSpan.FromDays(30) ? "1Mo" :
			throw new NotSupportedException(
				$"Bluefin does not support the {value} candle time-frame.");

	public static TimeSpan FromBluefinInterval(this string value)
		=> value switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"3m" => TimeSpan.FromMinutes(3),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"8h" => TimeSpan.FromHours(8),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			"1Mo" => TimeSpan.FromDays(30),
			_ => throw new InvalidDataException(
				$"Bluefin returned unknown candle interval '{value}'."),
		};
}

sealed class BluefinApiException : InvalidOperationException
{
	public BluefinApiException(string message)
		: base(message)
	{
	}

	public BluefinApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
