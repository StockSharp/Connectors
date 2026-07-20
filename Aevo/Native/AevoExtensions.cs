namespace StockSharp.Aevo.Native;

static class AevoExtensions
{
	public const decimal WirePrecision = 1_000_000m;

	public static string RestEndpoint(this AevoEnvironments environment)
		=> environment == AevoEnvironments.Testnet
			? "https://api-testnet.aevo.xyz"
			: "https://api.aevo.xyz";

	public static string SocketEndpoint(this AevoEnvironments environment)
		=> environment == AevoEnvironments.Testnet
			? "wss://ws-testnet.aevo.xyz"
			: "wss://ws.aevo.xyz";

	public static string DomainName(this AevoEnvironments environment)
		=> environment == AevoEnvironments.Testnet
			? "Aevo Testnet"
			: "Aevo Mainnet";

	public static long ChainId(this AevoEnvironments environment)
		=> environment == AevoEnvironments.Testnet ? 11155111 : 1;

	public static string NormalizeAddress(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 40 || !value.All(Uri.IsHexDigit))
			throw new FormatException($"Invalid EVM address '{value}'.");
		return "0x" + value.ToLowerInvariant();
	}

	public static string NormalizeOrderId(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || !value.All(Uri.IsHexDigit))
			throw new FormatException("Aevo order ID must be a 32-byte hash.");
		return "0x" + value.ToLowerInvariant();
	}

	public static string NormalizeHttpEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Aevo REST endpoint must be an absolute HTTPS URI.", name);
		return value.TrimEnd('/') + "/";
	}

	public static Uri NormalizeSocketEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Aevo WebSocket endpoint must be an absolute WSS URI.", name);
		return uri;
	}

	public static decimal ParseAevoDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Aevo returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParseAevoDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static DateTime FromAevoNanoseconds(this string value)
	{
		if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var nanoseconds) || nanoseconds <= 0)
			throw new InvalidDataException(
				$"Aevo returned invalid timestamp '{value}'.");
		return DateTime.UnixEpoch.AddTicks(nanoseconds / 100);
	}

	public static long ToAevoNanoseconds(this DateTime value)
	{
		value = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		return checked((value - DateTime.UnixEpoch).Ticks * 100L);
	}

	public static string ToWire(this decimal value, string field)
	{
		var scaled = value * WirePrecision;
		if (value < 0 || scaled != decimal.Truncate(scaled))
			throw new ArgumentOutOfRangeException(field, value,
				"Aevo values must be non-negative with no more than six decimals.");
		return decimal.Truncate(scaled).ToString("0", CultureInfo.InvariantCulture);
	}

	public static SecurityTypes ToStockSharp(this AevoInstrumentTypes type)
		=> type switch
		{
			AevoInstrumentTypes.Option => SecurityTypes.Option,
			AevoInstrumentTypes.Perpetual => SecurityTypes.Future,
			AevoInstrumentTypes.Spot => SecurityTypes.CryptoCurrency,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static OptionTypes ToStockSharp(this AevoOptionTypes type)
		=> type == AevoOptionTypes.Call ? OptionTypes.Call : OptionTypes.Put;

	public static Sides ToStockSharpSide(this string value)
		=> value.EqualsIgnoreCase("buy")
			? Sides.Buy
			: value.EqualsIgnoreCase("sell")
				? Sides.Sell
				: throw new InvalidDataException(
					$"Aevo returned invalid side '{value}'.");

	public static OrderStates ToStockSharpState(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"opened" or "partial" => OrderStates.Active,
			"filled" or "cancelled" or "canceled" or "expired" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.Aevo,
		};

	public static CurrencyTypes? ToAevoCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string ChannelSymbol(this string channel)
	{
		var separator = channel?.IndexOf(':') ?? -1;
		return separator >= 0 && separator + 1 < channel.Length
			? channel[(separator + 1)..]
			: null;
	}

	public static uint CalculateChecksum(string[][] bids, string[][] asks)
	{
		var parts = new List<string>(400);
		var count = Math.Min(Math.Max(bids?.Length ?? 0, asks?.Length ?? 0), 100);
		for (var index = 0; index < count; index++)
		{
			if (index < (bids?.Length ?? 0) && bids[index]?.Length >= 2)
			{
				parts.Add(bids[index][0]);
				parts.Add(bids[index][1]);
			}
			if (index < (asks?.Length ?? 0) && asks[index]?.Length >= 2)
			{
				parts.Add(asks[index][0]);
				parts.Add(asks[index][1]);
			}
		}
		var bytes = string.Join(":", parts).UTF8();
		var crc = uint.MaxValue;
		foreach (var value in bytes)
		{
			crc ^= value;
			for (var bit = 0; bit < 8; bit++)
				crc = (crc & 1) != 0
					? (crc >> 1) ^ 0xedb88320u
					: crc >> 1;
		}
		return ~crc;
	}
}
