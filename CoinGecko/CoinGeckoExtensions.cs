namespace StockSharp.CoinGecko;

readonly record struct CoinGeckoSecurityKey(
	CoinGeckoSecurityKinds Kind,
	string CoinId,
	string QuoteCurrency,
	string Network,
	string PoolAddress,
	string TokenAddress,
	string Symbol,
	string Name,
	string Dex)
{
	public string ToNative()
		=> string.Join('|', Kind.ToString(),
			Escape(CoinId), Escape(QuoteCurrency), Escape(Network),
			Escape(PoolAddress), Escape(TokenAddress), Escape(Symbol),
			Escape(Name), Escape(Dex));

	public static bool TryParse(string value, out CoinGeckoSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 9 ||
			!Enum.TryParse(parts[0], false, out CoinGeckoSecurityKinds kind) ||
			!Enum.IsDefined(kind))
			return false;
		key = new(kind, Unescape(parts[1]),
			Unescape(parts[2]), Unescape(parts[3]), Unescape(parts[4]),
			Unescape(parts[5]), Unescape(parts[6]), Unescape(parts[7]),
			Unescape(parts[8]));
		return key.Kind switch
		{
			CoinGeckoSecurityKinds.Coin => !key.CoinId.IsEmpty() &&
				!key.QuoteCurrency.IsEmpty(),
			CoinGeckoSecurityKinds.OnchainPool => !key.Network.IsEmpty() &&
				!key.PoolAddress.IsEmpty() && !key.TokenAddress.IsEmpty(),
			_ => false,
		};
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}

static class CoinGeckoExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(4),
	];

	public static string GetApiEndpoint(this CoinGeckoApiTiers tier)
		=> tier switch
		{
			CoinGeckoApiTiers.Demo => "https://api.coingecko.com/api/v3",
			CoinGeckoApiTiers.Pro => "https://pro-api.coingecko.com/api/v3",
			_ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
		};

	public static string GetApiKeyHeader(this CoinGeckoApiTiers tier)
		=> tier switch
		{
			CoinGeckoApiTiers.Demo => "x-cg-demo-api-key",
			CoinGeckoApiTiers.Pro => "x-cg-pro-api-key",
			_ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
		};

	public static DateTime ToCoinGeckoTime(this decimal value)
	{
		var milliseconds = Math.Abs(value) >= 100_000_000_000m
			? value
			: value * 1000m;
		return DateTime.UnixEpoch.AddMilliseconds((double)milliseconds);
	}

	public static DateTime ParseCoinGeckoTime(this string value)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException($"Invalid CoinGecko timestamp '{value}'.");
		return result.EnsureUtc();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static decimal? ParseCoinGeckoDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	public static string NormalizeCurrency(string value)
		=> value.ThrowIfEmpty(nameof(value)).Trim().ToLowerInvariant();

	public static SecurityId ToSecurityId(this CoinGeckoSecurityKey key)
		=> new()
		{
			SecurityCode = key.Kind == CoinGeckoSecurityKinds.Coin
				? $"{key.Symbol.ToUpperInvariant()}/{key.QuoteCurrency.ToUpperInvariant()}"
				: $"{key.Symbol.ToUpperInvariant()}/USD",
			BoardCode = key.Kind == CoinGeckoSecurityKinds.Coin
				? BoardCodes.CoinGecko
				: BoardCodes.CoinGeckoOnChain,
			Native = key.ToNative(),
		};

	public static CoinGeckoSocketIntervals ToSocketInterval(this TimeSpan value)
		=> value == TimeSpan.FromSeconds(1) ? CoinGeckoSocketIntervals.Second1 :
			value == TimeSpan.FromMinutes(1) ? CoinGeckoSocketIntervals.Minute1 :
			value == TimeSpan.FromMinutes(5) ? CoinGeckoSocketIntervals.Minute5 :
			value == TimeSpan.FromMinutes(15) ? CoinGeckoSocketIntervals.Minute15 :
			value == TimeSpan.FromHours(1) ? CoinGeckoSocketIntervals.Hour1 :
			value == TimeSpan.FromHours(2) ? CoinGeckoSocketIntervals.Hour2 :
			value == TimeSpan.FromHours(4) ? CoinGeckoSocketIntervals.Hour4 :
			value == TimeSpan.FromHours(8) ? CoinGeckoSocketIntervals.Hour8 :
			value == TimeSpan.FromHours(12) ? CoinGeckoSocketIntervals.Hour12 :
			value == TimeSpan.FromDays(1) ? CoinGeckoSocketIntervals.Day1 :
			throw new NotSupportedException(
				$"CoinGecko does not support the {value} candle interval.");

	public static TimeSpan ToTimeFrame(this CoinGeckoSocketIntervals value)
		=> value switch
		{
			CoinGeckoSocketIntervals.Second1 => TimeSpan.FromSeconds(1),
			CoinGeckoSocketIntervals.Minute1 => TimeSpan.FromMinutes(1),
			CoinGeckoSocketIntervals.Minute5 => TimeSpan.FromMinutes(5),
			CoinGeckoSocketIntervals.Minute15 => TimeSpan.FromMinutes(15),
			CoinGeckoSocketIntervals.Hour1 => TimeSpan.FromHours(1),
			CoinGeckoSocketIntervals.Hour2 => TimeSpan.FromHours(2),
			CoinGeckoSocketIntervals.Hour4 => TimeSpan.FromHours(4),
			CoinGeckoSocketIntervals.Hour8 => TimeSpan.FromHours(8),
			CoinGeckoSocketIntervals.Hour12 => TimeSpan.FromHours(12),
			CoinGeckoSocketIntervals.Day1 => TimeSpan.FromDays(1),
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static (CoinGeckoOhlcvTimeframes Timeframe, int Aggregate,
		TimeSpan SourceFrame) ToPoolHistoryInterval(this TimeSpan value)
		=> value == TimeSpan.FromSeconds(1)
			? (CoinGeckoOhlcvTimeframes.Second, 1, value) :
		value == TimeSpan.FromSeconds(15)
			? (CoinGeckoOhlcvTimeframes.Second, 15, value) :
		value == TimeSpan.FromSeconds(30)
			? (CoinGeckoOhlcvTimeframes.Second, 30, value) :
		value == TimeSpan.FromMinutes(1)
			? (CoinGeckoOhlcvTimeframes.Minute, 1, value) :
		value == TimeSpan.FromMinutes(5)
			? (CoinGeckoOhlcvTimeframes.Minute, 5, value) :
		value == TimeSpan.FromMinutes(15)
			? (CoinGeckoOhlcvTimeframes.Minute, 15, value) :
		value == TimeSpan.FromMinutes(30)
			? (CoinGeckoOhlcvTimeframes.Minute, 15, TimeSpan.FromMinutes(15)) :
		value == TimeSpan.FromHours(1)
			? (CoinGeckoOhlcvTimeframes.Hour, 1, value) :
		value == TimeSpan.FromHours(2)
			? (CoinGeckoOhlcvTimeframes.Hour, 1, TimeSpan.FromHours(1)) :
		value == TimeSpan.FromHours(4)
			? (CoinGeckoOhlcvTimeframes.Hour, 4, value) :
		value == TimeSpan.FromHours(8)
			? (CoinGeckoOhlcvTimeframes.Hour, 4, TimeSpan.FromHours(4)) :
		value == TimeSpan.FromHours(12)
			? (CoinGeckoOhlcvTimeframes.Hour, 12, value) :
		value == TimeSpan.FromDays(1)
			? (CoinGeckoOhlcvTimeframes.Day, 1, value) :
		value == TimeSpan.FromDays(4)
			? (CoinGeckoOhlcvTimeframes.Day, 1, TimeSpan.FromDays(1)) :
		throw new NotSupportedException(
			$"CoinGecko on-chain OHLCV does not support {value}.");

	public static DateTime Align(this DateTime value, TimeSpan timeFrame)
	{
		var ticks = value.EnsureUtc().Ticks - DateTime.UnixEpoch.Ticks;
		return DateTime.UnixEpoch.AddTicks(ticks / timeFrame.Ticks *
			timeFrame.Ticks);
	}

	public static string DeriveNetwork(string resourceId, string address)
	{
		if (resourceId.IsEmpty() || address.IsEmpty())
			return null;
		var suffix = "_" + address;
		return resourceId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
			? resourceId[..^suffix.Length]
			: null;
	}
}
