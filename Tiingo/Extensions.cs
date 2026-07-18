namespace StockSharp.Tiingo;

static class Extensions
{
	public const string StockBoard = "TIINGO";
	public const string ForexBoard = "TIINGOFX";
	public const string CryptoBoard = "TIINGOCRYPTO";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(45),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(8),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static int ToThreshold(this TiingoEquityStreamingModes mode)
		=> mode switch
		{
			TiingoEquityStreamingModes.ReferencePrice => 6,
			TiingoEquityStreamingModes.IexTop => 5,
			TiingoEquityStreamingModes.IexAll => 0,
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
		};

	public static string ToBoard(this TiingoMarkets market)
		=> market switch
		{
			TiingoMarkets.Stocks => StockBoard,
			TiingoMarkets.Forex => ForexBoard,
			TiingoMarkets.Crypto => CryptoBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static TiingoMarkets ToTiingoMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(ForexBoard) ? TiingoMarkets.Forex :
			boardCode.EqualsIgnoreCase(CryptoBoard) ? TiingoMarkets.Crypto :
			TiingoMarkets.Stocks;

	public static string ToIntradayResample(this TimeSpan timeFrame)
	{
		if (timeFrame <= TimeSpan.Zero || timeFrame >= TimeSpan.FromDays(1))
			throw new NotSupportedException($"Tiingo intraday API does not support {timeFrame}.");
		if (timeFrame.TotalMinutes < 60)
			return checked((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "min";
		return checked((int)timeFrame.TotalHours).ToString(CultureInfo.InvariantCulture) + "hour";
	}

	public static string ToEodResample(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(1) ? "daily" :
			timeFrame == TimeSpan.FromDays(7) ? "weekly" :
			timeFrame == TimeSpan.FromDays(30) ? "monthly" :
			throw new NotSupportedException($"Tiingo EOD API does not support {timeFrame}.");

	public static TiingoSecurityKey GetTiingoKey(this SecurityId securityId, string cryptoExchange)
	{
		var native = securityId.Native as string;
		if (TiingoSecurityKey.TryParse(native, out var key))
			return key;
		var market = securityId.BoardCode.ToTiingoMarket();
		var ticker = native.IsEmpty(securityId.SecurityCode).ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new(market, NormalizeTicker(ticker),
			market == TiingoMarkets.Crypto ? cryptoExchange : null);
	}

	public static SecurityId NormalizeTiingo(this SecurityId securityId, TiingoSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Ticker);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this TiingoSearchItem item,
		long originalTransactionId)
	{
		if (item?.Ticker.IsEmpty() != false)
			return null;
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = item.Ticker,
				BoardCode = StockBoard,
				Native = new TiingoSecurityKey(TiingoMarkets.Stocks, item.Ticker, null).ToNative(),
				Bloomberg = item.OpenFigi?.StartsWith("BBG", StringComparison.OrdinalIgnoreCase) == true
					? item.OpenFigi : null,
			},
			Name = item.Name.IsEmpty(item.Ticker),
			ShortName = item.Ticker,
			SecurityType = item.AssetType.ToSecurityType(),
		};
	}

	public static SecurityMessage ToSecurityMessage(this TiingoSupportedTicker item,
		long originalTransactionId)
	{
		if (item?.Ticker.IsEmpty() != false)
			return null;
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = item.Ticker,
				BoardCode = StockBoard,
				Native = new TiingoSecurityKey(TiingoMarkets.Stocks, item.Ticker, item.Exchange).ToNative(),
			},
			Name = item.Ticker,
			ShortName = item.Ticker,
			Class = item.Exchange,
			SecurityType = item.AssetType.ToSecurityType(),
		};
		if (Enum.TryParse<CurrencyTypes>(item.PriceCurrency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityMessage ToSecurityMessage(this TiingoEodMeta item,
		long originalTransactionId)
	{
		if (item?.Ticker.IsEmpty() != false)
			return null;
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = item.Ticker,
				BoardCode = StockBoard,
				Native = new TiingoSecurityKey(TiingoMarkets.Stocks, item.Ticker,
					item.ExchangeCode).ToNative(),
			},
			Name = item.Name.IsEmpty(item.Ticker),
			ShortName = item.Ticker,
			Class = item.ExchangeCode,
			SecurityType = SecurityTypes.Stock,
		};
	}

	public static SecurityMessage ToSecurityMessage(this TiingoCryptoMeta item,
		string exchange, long originalTransactionId)
	{
		if (item?.Ticker.IsEmpty() != false)
			return null;
		var code = FormatPair(item.BaseCurrency, item.QuoteCurrency, item.Ticker);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = CryptoBoard,
				Native = new TiingoSecurityKey(TiingoMarkets.Crypto, item.Ticker, exchange).ToNative(),
			},
			Name = item.Name.IsEmpty(item.Description).IsEmpty(code),
			ShortName = code,
			Class = exchange,
			SecurityType = SecurityTypes.CryptoCurrency,
		};
		if (Enum.TryParse<CurrencyTypes>(item.QuoteCurrency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityMessage ToSecurityMessage(this TiingoFxQuote item,
		long originalTransactionId)
	{
		if (item?.Ticker.IsEmpty() != false)
			return null;
		var code = FormatCompactPair(item.Ticker);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = ForexBoard,
				Native = new TiingoSecurityKey(TiingoMarkets.Forex, item.Ticker, null).ToNative(),
			},
			Name = code,
			ShortName = code,
			SecurityType = SecurityTypes.Currency,
		};
		var quote = item.Ticker?.Length == 6 ? item.Ticker[3..] : null;
		if (Enum.TryParse<CurrencyTypes>(quote, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static DateTime ParseUtc(string value)
	{
		if (!TryParseUtc(value, out var result))
			throw new InvalidDataException($"Invalid Tiingo timestamp '{value}'.");
		return result;
	}

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty())
			return false;
		value = TrimFraction(value);
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
		{
			return false;
		}
		result = parsed.UtcDateTime;
		return true;
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 100000) : 500;
		var factor = timeFrame < TimeSpan.FromDays(1) ? 3L : 2L;
		try
		{
			var ticks = checked(timeFrame.Ticks * bars * factor);
			var from = to - TimeSpan.FromTicks(ticks);
			return from < DateTime.UnixEpoch ? DateTime.UnixEpoch : from;
		}
		catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
		{
			return DateTime.UnixEpoch;
		}
	}

	public static TimeSpan GetHistoryWindow(this TimeSpan timeFrame)
		=> timeFrame <= TimeSpan.FromMinutes(1) ? TimeSpan.FromDays(7) :
			timeFrame <= TimeSpan.FromMinutes(5) ? TimeSpan.FromDays(31) :
			timeFrame <= TimeSpan.FromHours(1) ? TimeSpan.FromDays(180) :
			timeFrame < TimeSpan.FromDays(1) ? TimeSpan.FromDays(365) :
			TimeSpan.FromDays(3650);

	public static DateTime GetCandleCloseTime(DateTime openTime, TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(30) ? openTime.AddMonths(1) : openTime + timeFrame;

	public static string NormalizeTicker(string value)
		=> value?.Replace("/", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

	public static bool Matches(this TiingoSearchItem item, string value)
		=> value.IsEmpty() || item?.Ticker.ContainsIgnoreCase(value) == true ||
			item?.Name.ContainsIgnoreCase(value) == true ||
			item?.OpenFigi.ContainsIgnoreCase(value) == true;

	public static bool Matches(this TiingoSupportedTicker item, string value)
		=> value.IsEmpty() || item?.Ticker.ContainsIgnoreCase(value) == true ||
			item?.Exchange.ContainsIgnoreCase(value) == true;

	public static bool Matches(this TiingoCryptoMeta item, string value)
		=> value.IsEmpty() || item?.Ticker.ContainsIgnoreCase(value) == true ||
			item?.Name.ContainsIgnoreCase(value) == true ||
			item?.BaseCurrency.ContainsIgnoreCase(value) == true ||
			item?.QuoteCurrency.ContainsIgnoreCase(value) == true;

	public static bool Matches(this TiingoFxQuote item, string value)
		=> value.IsEmpty() || item?.Ticker.ContainsIgnoreCase(NormalizeTicker(value)) == true;

	private static SecurityTypes ToSecurityType(this string value)
		=> value.ContainsIgnoreCase("etf") ? SecurityTypes.Etf :
			value.ContainsIgnoreCase("mutual") || value.ContainsIgnoreCase("fund")
				? SecurityTypes.Fund : SecurityTypes.Stock;

	private static string FormatPair(string @base, string quote, string fallback)
		=> @base.IsEmpty() || quote.IsEmpty() ? fallback?.ToUpperInvariant() :
			$"{@base.ToUpperInvariant()}/{quote.ToUpperInvariant()}";

	private static string FormatCompactPair(string ticker)
		=> ticker?.Length == 6 ? $"{ticker[..3].ToUpperInvariant()}/{ticker[3..].ToUpperInvariant()}" :
			ticker?.ToUpperInvariant();

	private static string TrimFraction(string value)
	{
		var dot = value.IndexOf('.');
		if (dot < 0)
			return value;
		var end = dot + 1;
		while (end < value.Length && char.IsDigit(value[end]))
			end++;
		var digits = end - dot - 1;
		return digits <= 7 ? value : value.Remove(dot + 8, digits - 7);
	}
}

readonly record struct TiingoSecurityKey(TiingoMarkets Market, string Ticker, string Exchange)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture),
			Escape(Ticker), Escape(Exchange));

	public static bool TryParse(string value, out TiingoSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(TiingoMarkets), marketValue))
		{
			return false;
		}
		var ticker = Unescape(parts[1]);
		if (ticker.IsEmpty())
			return false;
		key = new((TiingoMarkets)marketValue, ticker, Unescape(parts[2]));
		return true;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}
