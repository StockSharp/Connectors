namespace StockSharp.CoinSwitch.Native;

static class CoinSwitchExtensions
{
	private static readonly PairSet<TimeSpan, int> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), 1 },
		{ TimeSpan.FromMinutes(5), 5 },
		{ TimeSpan.FromMinutes(15), 15 },
		{ TimeSpan.FromMinutes(30), 30 },
		{ TimeSpan.FromHours(1), 60 },
		{ TimeSpan.FromHours(2), 120 },
		{ TimeSpan.FromHours(4), 240 },
		{ TimeSpan.FromHours(6), 360 },
		{ TimeSpan.FromHours(12), 720 },
		{ TimeSpan.FromDays(1), 1440 },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static int ToCoinSwitchInterval(this TimeSpan timeFrame)
	{
		if (_timeFrames.TryGetValue(timeFrame, out var interval))
			return interval;
		throw new ArgumentOutOfRangeException(
			nameof(timeFrame),
			timeFrame,
			"Unsupported CoinSwitch candle interval.");
	}

	public static TimeSpan ToCoinSwitchTimeFrame(this int interval)
	{
		if (_timeFrames.TryGetKey(interval, out var timeFrame))
			return timeFrame;
		throw new ArgumentOutOfRangeException(
			nameof(interval),
			interval,
			"Unsupported CoinSwitch candle interval.");
	}

	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToCoinSwitchNativeSymbol(
		this string securityCode,
		CoinSwitchProductTypes productType)
	{
		securityCode = securityCode.ThrowIfEmpty(
			nameof(securityCode)).Trim();
		if (productType == CoinSwitchProductTypes.Options)
			return securityCode.ToUpperInvariant();
		var parts = securityCode.Split(
			['/', ',', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);
		if (parts.Length == 2)
			return productType == CoinSwitchProductTypes.Spot
				? $"{parts[0].ToUpperInvariant()}/" +
					parts[1].ToUpperInvariant()
				: (parts[0] + parts[1]).ToUpperInvariant();
		if (securityCode.All(char.IsLetterOrDigit))
			return securityCode.ToUpperInvariant();
		throw new FormatException(
			$"Invalid CoinSwitch symbol '{securityCode}'.");
	}

	public static string ToCoinSwitchSocketSymbol(
		this string securityCode,
		CoinSwitchProductTypes productType)
		=> productType == CoinSwitchProductTypes.Spot
			? securityCode
				.ToCoinSwitchNativeSymbol(productType)
				.Replace('/', ',')
			: securityCode.ToCoinSwitchNativeSymbol(productType);

	public static string ToCoinSwitchSecurityCode(
		this string nativeSymbol,
		string quoteCurrency)
	{
		nativeSymbol = nativeSymbol.ThrowIfEmpty(
			nameof(nativeSymbol)).Trim().ToUpperInvariant();
		quoteCurrency = quoteCurrency.ThrowIfEmpty(
			nameof(quoteCurrency)).Trim().ToUpperInvariant();
		if (nativeSymbol.Contains('/') ||
			nativeSymbol.Contains(','))
		{
			var parts = nativeSymbol.Split(['/', ',']);
			if (parts.Length == 2)
				return CreateSecurityCode(parts[0], parts[1]);
		}
		if (!nativeSymbol.EndsWith(
			quoteCurrency,
			StringComparison.OrdinalIgnoreCase) ||
			nativeSymbol.Length <= quoteCurrency.Length)
			throw new FormatException(
				$"CoinSwitch symbol '{nativeSymbol}' does not " +
					$"end with '{quoteCurrency}'.");
		return CreateSecurityCode(
			nativeSymbol[..^quoteCurrency.Length],
			quoteCurrency);
	}

	public static decimal? GetStep(int precision)
	{
		if (precision is < -28 or > 28)
			return null;
		var step = 1m;
		if (precision >= 0)
			for (var index = 0; index < precision; index++)
				step /= 10m;
		else
			for (var index = 0; index > precision; index--)
				step *= 10m;
		return step;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToCoinSwitch(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToSpotOrderState(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"OPEN" or "PARTIALLY_EXECUTED" or
				"CANCELLATION_RAISED" or
				"EXPIRATION_RAISED" => OrderStates.Active,
			"EXECUTED" or "CANCELLED" or "EXPIRED" =>
				OrderStates.Done,
			"DISCARDED" or "REJECTED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToFuturesOrderState(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"RAISED" or "NEW" or "CANCELLATION_RAISED" =>
				OrderStates.Active,
			"EXECUTED" or "FILLED" or "PARTIALLY_EXECUTED" or
				"PARTIALLYFILLED" or "CANCELLED" =>
				OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToHftOrderState(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"NEW" or "CREATED" or "UNTRIGGERED" or
				"PARTIALLYFILLED" => OrderStates.Active,
			"FILLED" or "CANCELLED" or "DEACTIVATED" or
				"TRIGGERED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP_MARKET" or "TAKE_PROFIT_MARKET" =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce? ToTimeInForce(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"GTC" or "GOODTILCANCEL" => TimeInForce.PutInQueue,
			"IOC" or "IMMEDIATEORCANCEL" => TimeInForce.MatchOrCancel,
			"FOK" or "FILLORKILL" => TimeInForce.CancelBalance,
			_ => null,
		};

	public static DateTime FromCoinSwitchMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static long ToCoinSwitchMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	internal static OptionTypes? ParseOptionType(string symbol)
	{
		var suffix = symbol?.Split('-').LastOrDefault();
		return suffix?.ToUpperInvariant() switch
		{
			"C" => OptionTypes.Call,
			"P" => OptionTypes.Put,
			_ => null,
		};
	}

	internal static decimal? ParseOptionStrike(string symbol)
	{
		var parts = symbol?.Split('-');
		return parts is { Length: >= 4 } &&
			decimal.TryParse(
				parts[^2],
				NumberStyles.Number,
				CultureInfo.InvariantCulture,
				out var strike)
					? strike
					: null;
	}
}
