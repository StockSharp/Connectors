namespace StockSharp.EodHistoricalData;

static class Extensions
{
	public const string StockBoard = "EODHD";
	public const string ForexBoard = "EODHDFX";
	public const string CryptoBoard = "EODHDCC";
	public const string OptionBoard = "EODHDOPT";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToBoard(this EodhdMarkets market)
		=> market switch
		{
			EodhdMarkets.Stocks => StockBoard,
			EodhdMarkets.Forex => ForexBoard,
			EodhdMarkets.Crypto => CryptoBoard,
			EodhdMarkets.Options => OptionBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static EodhdMarkets ToEodhdMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(ForexBoard) ? EodhdMarkets.Forex :
			boardCode.EqualsIgnoreCase(CryptoBoard) ? EodhdMarkets.Crypto :
			boardCode.EqualsIgnoreCase(OptionBoard) ? EodhdMarkets.Options :
			EodhdMarkets.Stocks;

	public static EodhdMarkets ToEodhdMarketByExchange(this string exchange)
		=> exchange.EqualsIgnoreCase("FOREX") ? EodhdMarkets.Forex :
			exchange.EqualsIgnoreCase("CC") ? EodhdMarkets.Crypto : EodhdMarkets.Stocks;

	public static EodhdSecurityKey GetEodhdKey(this SecurityId securityId,
		string stockExchange)
	{
		var native = securityId.Native as string;
		if (EodhdSecurityKey.TryParse(native, out var key))
			return key;

		var market = securityId.BoardCode.ToEodhdMarket();
		var code = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode)).Trim();
		var exchange = market switch
		{
			EodhdMarkets.Forex => "FOREX",
			EodhdMarkets.Crypto => "CC",
			EodhdMarkets.Options => "OPTIONS",
			_ => stockExchange.ThrowIfEmpty(nameof(stockExchange)),
		};
		if (market != EodhdMarkets.Options)
			SplitFullTicker(ref code, ref exchange);
		return new(market, NormalizeCode(code, market), exchange.ToUpperInvariant(), null);
	}

	public static SecurityId NormalizeEodhd(this SecurityId securityId,
		EodhdSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Code);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static string ToRestTicker(this EodhdSecurityKey key)
	{
		if (key.Market == EodhdMarkets.Options)
			throw new InvalidOperationException("Options use the EODHD marketplace API.");
		var suffix = "." + key.Exchange;
		return key.Code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
			? key.Code : key.Code + suffix;
	}

	public static string ToStreamSymbol(this EodhdSecurityKey key)
		=> key.Market switch
		{
			EodhdMarkets.Stocks => StripExchange(key.Code, key.Exchange),
			EodhdMarkets.Forex => StripExchange(key.Code, key.Exchange)
				.Replace("/", string.Empty, StringComparison.Ordinal)
				.Replace("-", string.Empty, StringComparison.Ordinal),
			EodhdMarkets.Crypto => StripExchange(key.Code, key.Exchange)
				.Replace("/", "-", StringComparison.Ordinal),
			_ => throw new NotSupportedException("EODHD options have no WebSocket product."),
		};

	public static bool IsUsStock(this EodhdSecurityKey key)
		=> key.Market == EodhdMarkets.Stocks && key.Exchange.EqualsIgnoreCase("US");

	public static SecurityMessage ToSecurityMessage(this EodhdSymbol item,
		string requestedExchange, long originalTransactionId)
	{
		if (item?.Code.IsEmpty() != false)
			return null;
		var exchange = item.Exchange.IsEmpty(requestedExchange).ToUpperInvariant();
		var market = exchange.ToEodhdMarketByExchange();
		return CreateSecurity(item.Code, item.Name, item.Type, item.Currency, item.Isin,
			exchange, market, originalTransactionId);
	}

	public static SecurityMessage ToSecurityMessage(this EodhdSearchItem item,
		long originalTransactionId)
	{
		if (item?.Code.IsEmpty() != false || item.Exchange.IsEmpty())
			return null;
		var exchange = item.Exchange.ToUpperInvariant();
		return CreateSecurity(item.Code, item.Name, item.Type, item.Currency, item.Isin,
			exchange, exchange.ToEodhdMarketByExchange(), originalTransactionId);
	}

	public static SecurityMessage ToSecurityMessage(this EodhdOptionResource item,
		long originalTransactionId)
	{
		var value = item?.Attributes;
		if (value == null)
			return null;
		var contract = value?.Contract.IsEmpty(item?.Id);
		if (contract.IsEmpty())
			return null;

		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = contract,
				BoardCode = OptionBoard,
				Native = new EodhdSecurityKey(EodhdMarkets.Options, contract,
					"OPTIONS", value.UnderlyingSymbol).ToNative(),
			},
			Name = contract,
			ShortName = contract,
			Class = value.Exchange,
			SecurityType = SecurityTypes.Option,
			Strike = value.Strike,
			OptionType = value.OptionType.ToOptionType(),
			ExpiryDate = TryParseDate(value.ExpirationDate, out var expiry) ? expiry : null,
		};
		if (!value.UnderlyingSymbol.IsEmpty())
		{
			message.UnderlyingSecurityId = new()
			{
				SecurityCode = value.UnderlyingSymbol,
				BoardCode = StockBoard,
			};
		}
		if (Enum.TryParse<CurrencyTypes>(value.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static string ToIntradayInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5m" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15m" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30m" :
			timeFrame == TimeSpan.FromHours(1) ? "1h" :
			throw new NotSupportedException($"EODHD intraday API does not support {timeFrame}.");

	public static string ToEodPeriod(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(1) ? "d" :
			timeFrame == TimeSpan.FromDays(7) ? "w" :
			timeFrame == TimeSpan.FromDays(30) ? "m" :
			throw new NotSupportedException($"EODHD end-of-day API does not support {timeFrame}.");

	public static DateTime GetCandleCloseTime(DateTime openTime, TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(30) ? openTime.AddMonths(1) : openTime + timeFrame;

	public static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 100000) : 500;
		var factor = timeFrame < TimeSpan.FromDays(1) ? 3L : 2L;
		try
		{
			var from = to - TimeSpan.FromTicks(checked(timeFrame.Ticks * bars * factor));
			return from < DateTime.UnixEpoch ? DateTime.UnixEpoch : from;
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

	public static DateTime FromUnixSeconds(long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddSeconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException($"Invalid EODHD Unix timestamp '{value}'.", error);
		}
	}

	public static DateTime FromUnixMilliseconds(long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException($"Invalid EODHD Unix timestamp '{value}'.", error);
		}
	}

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
		{
			return false;
		}
		result = parsed.UtcDateTime;
		return true;
	}

	public static bool TryParseDate(string value, out DateTime result)
	{
		if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out result))
		{
			result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
			return true;
		}
		result = default;
		return false;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value.EqualsIgnoreCase("call") ? OptionTypes.Call :
			value.EqualsIgnoreCase("put") ? OptionTypes.Put : null;

	public static string ToNative(this OptionTypes value)
		=> value switch
		{
			OptionTypes.Call => "call",
			OptionTypes.Put => "put",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string GetNewsSource(string link)
		=> Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.Host : null;

	public static string NormalizeCode(string code, EodhdMarkets market)
	{
		code = code?.Trim().ToUpperInvariant();
		return market switch
		{
			EodhdMarkets.Forex => code?.Replace("/", string.Empty, StringComparison.Ordinal)
				.Replace("-", string.Empty, StringComparison.Ordinal),
			EodhdMarkets.Crypto => code?.Replace("/", "-", StringComparison.Ordinal),
			_ => code,
		};
	}

	private static SecurityMessage CreateSecurity(string code, string name, string type,
		string currencyCode, string isin, string exchange, EodhdMarkets market,
		long originalTransactionId)
	{
		code = NormalizeCode(code, market);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = market.ToBoard(),
				Native = new EodhdSecurityKey(market, code, exchange, null).ToNative(),
				Isin = isin,
			},
			Name = name.IsEmpty(code),
			ShortName = code,
			Class = exchange,
			SecurityType = ToSecurityType(type, market),
		};
		if (Enum.TryParse<CurrencyTypes>(currencyCode, true, out var currency))
			message.Currency = currency;
		return message;
	}

	private static SecurityTypes ToSecurityType(string value, EodhdMarkets market)
	{
		if (market == EodhdMarkets.Forex)
			return SecurityTypes.Currency;
		if (market == EodhdMarkets.Crypto)
			return SecurityTypes.CryptoCurrency;
		return value.ContainsIgnoreCase("etf") ? SecurityTypes.Etf :
			value.ContainsIgnoreCase("fund") ? SecurityTypes.Fund :
			value.ContainsIgnoreCase("index") ? SecurityTypes.Index :
			value.ContainsIgnoreCase("bond") ? SecurityTypes.Bond :
			value.ContainsIgnoreCase("future") ? SecurityTypes.Future : SecurityTypes.Stock;
	}

	private static void SplitFullTicker(ref string code, ref string exchange)
	{
		var suffix = code.LastIndexOf('.');
		var suffixLength = code.Length - suffix - 1;
		if (suffix <= 0 || suffixLength is < 2 or > 10)
			return;
		exchange = code[(suffix + 1)..];
		code = code[..suffix];
	}

	private static string StripExchange(string code, string exchange)
	{
		var suffix = "." + exchange;
		return code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
			? code[..^suffix.Length] : code;
	}
}

readonly record struct EodhdSecurityKey(EodhdMarkets Market, string Code, string Exchange,
	string Underlying)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture),
			Escape(Code), Escape(Exchange), Escape(Underlying));

	public static bool TryParse(string value, out EodhdSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 4 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(EodhdMarkets), marketValue))
		{
			return false;
		}
		var code = Unescape(parts[1]);
		var exchange = Unescape(parts[2]);
		if (code.IsEmpty() || exchange.IsEmpty())
			return false;
		key = new((EodhdMarkets)marketValue, code, exchange, Unescape(parts[3]));
		return true;
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Unescape(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}
