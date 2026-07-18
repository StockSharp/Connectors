namespace StockSharp.Marketstack;

static class Extensions
{
	public const string BoardCode = "MARKETSTACK";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string ToMarketstackInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1min" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5min" :
			timeFrame == TimeSpan.FromMinutes(10) ? "10min" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15min" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30min" :
			timeFrame == TimeSpan.FromHours(1) ? "1hour" :
			throw new NotSupportedException(
				$"Marketstack intraday API does not support {timeFrame}.");

	public static MarketstackSecurityKey GetMarketstackKey(this SecurityId securityId,
		string stockExchange)
	{
		var native = securityId.Native as string;
		if (MarketstackSecurityKey.TryParse(native, out var key))
			return key;
		var symbol = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new(Normalize(symbol), Normalize(stockExchange), null);
	}

	public static SecurityId NormalizeMarketstack(this SecurityId securityId,
		MarketstackSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(BoardCode);
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this MarketstackTicker ticker,
		long originalTransactionId)
	{
		if (ticker?.Ticker.IsEmpty() != false)
			return null;
		var symbol = Normalize(ticker.Ticker);
		var mic = Normalize(ticker.StockExchange?.Mic);
		var acronym = Normalize(ticker.StockExchange?.Acronym);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = BoardCode,
				Native = new MarketstackSecurityKey(symbol, mic, acronym).ToNative(),
			},
			Name = ticker.Name.IsEmpty(symbol),
			ShortName = symbol,
			Class = mic.IsEmpty(acronym),
			SecurityType = SecurityTypes.Stock,
		};
	}

	public static bool Matches(this MarketstackTicker ticker, string value)
		=> value.IsEmpty() || ticker?.Ticker.ContainsIgnoreCase(value) == true ||
			ticker?.Name.ContainsIgnoreCase(value) == true;

	public static bool Matches(this MarketstackTicker ticker, MarketstackSecurityKey key)
	{
		if (ticker?.Ticker.EqualsIgnoreCase(key.Symbol) != true)
			return false;
		return key.Mic.IsEmpty() ||
			ticker.StockExchange?.Mic.EqualsIgnoreCase(key.Mic) == true;
	}

	public static bool Matches(this MarketstackBar bar, MarketstackSecurityKey key)
		=> bar?.Symbol.EqualsIgnoreCase(key.Symbol) == true &&
			(key.Mic.IsEmpty() || bar.Exchange.EqualsIgnoreCase(key.Mic));

	public static bool Matches(this MarketstackStockPrice price,
		MarketstackSecurityKey key)
	{
		if (price?.Ticker.EqualsIgnoreCase(key.Symbol) != true)
			return false;
		return key.ExchangeCode.IsEmpty() ||
			price.ExchangeCode.EqualsIgnoreCase(key.ExchangeCode);
	}

	public static bool TryGetOhlc(this MarketstackBar bar,
		MarketstackAdjustments adjustment, out decimal open, out decimal high,
		out decimal low, out decimal close, out decimal volume)
	{
		var isAdjusted = adjustment == MarketstackAdjustments.Adjusted &&
			bar.AdjustedOpen != null && bar.AdjustedHigh != null &&
			bar.AdjustedLow != null && bar.AdjustedClose != null;
		var o = isAdjusted ? bar.AdjustedOpen : bar.Open;
		var h = isAdjusted ? bar.AdjustedHigh : bar.High;
		var l = isAdjusted ? bar.AdjustedLow : bar.Low;
		var c = isAdjusted ? bar.AdjustedClose : bar.Close;
		open = o.GetValueOrDefault();
		high = h.GetValueOrDefault();
		low = l.GetValueOrDefault();
		close = c.GetValueOrDefault();
		volume = (isAdjusted ? bar.AdjustedVolume ?? bar.Volume : bar.Volume)
			.GetValueOrDefault().Max(0m);
		return o != null && h != null && l != null && c != null;
	}

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || !DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal |
			DateTimeStyles.AdjustToUniversal, out result))
		{
			return false;
		}
		result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
		return true;
	}

	public static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	public static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 100000) : 500;
		var factor = timeFrame < TimeSpan.FromDays(1) ? 3L : 2L;
		try
		{
			var result = to - TimeSpan.FromTicks(
				checked(timeFrame.Ticks * bars * factor));
			return result < DateTime.UnixEpoch ? DateTime.UnixEpoch : result;
		}
		catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
		{
			return DateTime.UnixEpoch;
		}
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	private static string Normalize(string value)
		=> value?.Trim().ToUpperInvariant();
}

readonly record struct MarketstackSecurityKey(string Symbol, string Mic, string ExchangeCode)
{
	public string ToNative()
		=> string.Join('|', Escape(Symbol), Escape(Mic), Escape(ExchangeCode));

	public static bool TryParse(string value, out MarketstackSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 3)
			return false;
		var symbol = Unescape(parts[0]);
		if (symbol.IsEmpty())
			return false;
		key = new(symbol, Unescape(parts[1]), Unescape(parts[2]));
		return true;
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Unescape(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}
