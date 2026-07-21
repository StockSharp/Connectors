namespace StockSharp.Polymarket.Native;

static class PolymarketExtensions
{
	public const decimal WirePrecision = 1_000_000m;

	public static string NormalizeAddress(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 40 || !value.All(Uri.IsHexDigit))
			throw new FormatException($"Invalid EVM address '{value}'.");
		return "0x" + value.ToLowerInvariant();
	}

	public static string NormalizeBytes32(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || !value.All(Uri.IsHexDigit))
			throw new FormatException($"{name} must be a 32-byte hexadecimal value.");
		return "0x" + value.ToLowerInvariant();
	}

	public static string NormalizeHttpEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!value.Contains("://", StringComparison.Ordinal))
			value = "https://" + value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Polymarket REST endpoints must be absolute HTTPS URIs.", name);
		return value.TrimEnd('/') + "/";
	}

	public static string NormalizeSocketEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!value.Contains("://", StringComparison.Ordinal))
			value = "wss://" + value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Polymarket WebSocket endpoints must be absolute WSS URIs.", name);
		return value;
	}

	public static decimal ParsePolymarketDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Polymarket returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParsePolymarketDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static DateTime ParsePolymarketMilliseconds(this string value)
	{
		if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var timestamp) && timestamp > 0)
			return DateTime.UnixEpoch.AddMilliseconds(timestamp);
		return DateTime.UtcNow;
	}

	public static DateTime ParsePolymarketSeconds(this string value,
		string field)
	{
		if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var timestamp) || timestamp <= 0)
			throw new InvalidDataException(
				$"Polymarket returned invalid {field} '{value}'.");
		return DateTime.UnixEpoch.AddSeconds(timestamp);
	}

	public static DateTime? TryParsePolymarketTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			return null;
		return result.EnsureUtc();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc
			? value
			: value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

	public static long ToPolymarketSeconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalSeconds);

	public static long ToPolymarketMilliseconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalMilliseconds);

	public static string ToWire(this decimal value, string field)
	{
		var scaled = value * WirePrecision;
		if (value < 0 || scaled != decimal.Truncate(scaled))
			throw new ArgumentOutOfRangeException(field, value,
				"Polymarket amounts must have no more than six decimals.");
		return decimal.Truncate(scaled).ToString("0", CultureInfo.InvariantCulture);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 28 || value < (BigInteger)decimal.MinValue ||
			value > (BigInteger)decimal.MaxValue)
			throw new InvalidDataException(
				"Polymarket integer amount is outside the decimal range.");
		var factor = 1m;
		for (var index = 0; index < decimals; index++)
			factor *= 10m;
		return (decimal)value / factor;
	}

	public static Sides ToStockSharp(this PolymarketSides side)
		=> side == PolymarketSides.Buy ? Sides.Buy : Sides.Sell;

	public static PolymarketSides ToPolymarket(this Sides side)
		=> side == Sides.Buy ? PolymarketSides.Buy : PolymarketSides.Sell;

	public static TimeInForce ToStockSharp(this PolymarketOrderTypes type)
		=> type switch
		{
			PolymarketOrderTypes.FillOrKill => TimeInForce.MatchOrCancel,
			PolymarketOrderTypes.FillAndKill => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToStockSharpOrderState(
		this PolymarketOrderStatuses value)
		=> value switch
		{
			PolymarketOrderStatuses.Live or
			PolymarketOrderStatuses.Unmatched or
			PolymarketOrderStatuses.Delayed => OrderStates.Active,
			PolymarketOrderStatuses.Matched or
			PolymarketOrderStatuses.Filled or
			PolymarketOrderStatuses.Canceled or
			PolymarketOrderStatuses.Cancelled =>
				OrderStates.Done,
			PolymarketOrderStatuses.Failed or
			PolymarketOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static OrderStates ToStockSharpOrderState(
		this PolymarketSocketStatuses? value)
		=> value switch
		{
			PolymarketSocketStatuses.Live or
			PolymarketSocketStatuses.Unmatched or
			PolymarketSocketStatuses.Delayed => OrderStates.Active,
			PolymarketSocketStatuses.Matched or
			PolymarketSocketStatuses.Canceled => OrderStates.Done,
			PolymarketSocketStatuses.Failed or
			PolymarketSocketStatuses.TradeStatusFailed => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static SecurityId ToStockSharp(this PolymarketMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Polymarket,
			Native = market.TokenId,
		};

	public static string NormalizeOrderId(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var hex = value[2..];
			if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
				throw new FormatException(
					"Polymarket order ID must be a 32-byte hash.");
			return "0x" + hex.ToLowerInvariant();
		}
		return value;
	}

	public static string ToSecurityCode(string slug, string outcome,
		string tokenId)
	{
		var prefix = slug.IsEmpty() ? "market" : slug.Trim().ToLowerInvariant();
		var suffix = outcome.IsEmpty() ? "outcome" : outcome.Trim()
			.ToLowerInvariant();
		var chars = (prefix + ":" + suffix).Select(static character =>
			char.IsLetterOrDigit(character) || character is '-' or '_' or ':'
				? character
				: '-').ToArray();
		var code = new string(chars).Trim('-');
		return code.IsEmpty()
			? "market:" + tokenId[^Math.Min(8, tokenId.Length)..]
			: code;
	}
}
