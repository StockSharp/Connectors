namespace StockSharp.TwelveData;

static class Extensions
{
	public const string StockBoard = "TWELVEDATA";
	public const string EtfBoard = "TWELVEDATAETF";
	public const string ForexBoard = "TWELVEDATAFX";
	public const string CryptoBoard = "TWELVEDATACRYPTO";
	public const string CommodityBoard = "TWELVEDATACMDTY";

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

	public static string ToNative(this TwelveDataAdjustments adjustment)
		=> adjustment switch
		{
			TwelveDataAdjustments.All => "all",
			TwelveDataAdjustments.Splits => "splits",
			TwelveDataAdjustments.Dividends => "dividends",
			TwelveDataAdjustments.None => "none",
			_ => throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment, null),
		};

	public static string ToNativeInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1min" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5min" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15min" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30min" :
			timeFrame == TimeSpan.FromMinutes(45) ? "45min" :
			timeFrame == TimeSpan.FromHours(1) ? "1h" :
			timeFrame == TimeSpan.FromHours(2) ? "2h" :
			timeFrame == TimeSpan.FromHours(4) ? "4h" :
			timeFrame == TimeSpan.FromHours(8) ? "8h" :
			timeFrame == TimeSpan.FromDays(1) ? "1day" :
			timeFrame == TimeSpan.FromDays(7) ? "1week" :
			timeFrame == TimeSpan.FromDays(30) ? "1month" :
			throw new NotSupportedException($"Twelve Data does not support {timeFrame} candles.");

	public static string ToBoard(this TwelveDataMarkets market)
		=> market switch
		{
			TwelveDataMarkets.Stocks => StockBoard,
			TwelveDataMarkets.Etfs => EtfBoard,
			TwelveDataMarkets.Forex => ForexBoard,
			TwelveDataMarkets.Crypto => CryptoBoard,
			TwelveDataMarkets.Commodities => CommodityBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static TwelveDataMarkets ToMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(EtfBoard) ? TwelveDataMarkets.Etfs :
			boardCode.EqualsIgnoreCase(ForexBoard) ? TwelveDataMarkets.Forex :
			boardCode.EqualsIgnoreCase(CryptoBoard) ? TwelveDataMarkets.Crypto :
			boardCode.EqualsIgnoreCase(CommodityBoard) ? TwelveDataMarkets.Commodities :
			TwelveDataMarkets.Stocks;

	public static TwelveDataSecurityKey GetTwelveDataKey(this SecurityId securityId,
		string stockExchange, string stockMic, string cryptoExchange)
	{
		var native = securityId.Native as string;
		if (TwelveDataSecurityKey.TryParse(native, out var key))
			return key;

		var market = securityId.BoardCode.ToMarket();
		var symbol = native.IsEmpty(securityId.SecurityCode).ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new(market, symbol,
			market is TwelveDataMarkets.Stocks or TwelveDataMarkets.Etfs ? stockExchange :
			market == TwelveDataMarkets.Crypto ? cryptoExchange : null,
			market is TwelveDataMarkets.Stocks or TwelveDataMarkets.Etfs ? stockMic : null);
	}

	public static SecurityId NormalizeTwelveData(this SecurityId securityId,
		TwelveDataSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this TwelveDataReferenceItem item,
		TwelveDataMarkets market, string preferredCryptoExchange, long originalTransactionId)
	{
		if (item == null || item.Symbol.IsEmpty())
			return null;

		var exchange = item.Exchange;
		if (market == TwelveDataMarkets.Crypto)
		{
			exchange = item.AvailableExchanges?.FirstOrDefault(value =>
				value.EqualsIgnoreCase(preferredCryptoExchange))
				.IsEmpty(item.Exchange).IsEmpty(preferredCryptoExchange)
				.IsEmpty(item.AvailableExchanges?.FirstOrDefault());
		}

		var key = new TwelveDataSecurityKey(market, item.Symbol, exchange, item.MicCode);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = item.Symbol,
				BoardCode = market.ToBoard(),
				Native = key.ToNative(),
				Bloomberg = SanitizeIdentifier(item.FigiCode),
				Isin = SanitizeIdentifier(item.Isin),
				Cusip = SanitizeIdentifier(item.Cusip),
			},
			Name = item.Name.IsEmpty(item.InstrumentName).IsEmpty(item.Description).IsEmpty(item.Symbol),
			ShortName = item.Symbol,
			Class = item.MicCode.IsEmpty(exchange).IsEmpty(item.Category),
			SecurityType = market.ToSecurityType(item.Type.IsEmpty(item.InstrumentType)),
		};

		var currency = item.Currency;
		if (currency.IsEmpty())
			currency = item.CurrencyQuote;
		if (currency.IsEmpty())
		{
			var pair = item.Symbol.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (pair.Length == 2)
				currency = pair[1];
		}
		if (Enum.TryParse<CurrencyTypes>(currency, true, out var currencyType))
			message.Currency = currencyType;
		return message;
	}

	public static TwelveDataMarkets GetMarket(this TwelveDataReferenceItem item)
	{
		var type = item?.InstrumentType.IsEmpty(item?.Type);
		if (type.ContainsIgnoreCase("etf") || type.ContainsIgnoreCase("exchange traded"))
			return TwelveDataMarkets.Etfs;
		if (type.ContainsIgnoreCase("crypto") || type.ContainsIgnoreCase("digital"))
			return TwelveDataMarkets.Crypto;
		if (item?.Category.IsEmpty() == false || IsCommoditySymbol(item?.Symbol))
			return TwelveDataMarkets.Commodities;
		if (type.ContainsIgnoreCase("forex") || type.ContainsIgnoreCase("currency"))
			return TwelveDataMarkets.Forex;
		if (type.ContainsIgnoreCase("commodity") || type.ContainsIgnoreCase("metal"))
			return TwelveDataMarkets.Commodities;
		return TwelveDataMarkets.Stocks;
	}

	public static bool Matches(this TwelveDataReferenceItem item, string value)
		=> value.IsEmpty() || item.Symbol.ContainsIgnoreCase(value) ||
			item.Name.ContainsIgnoreCase(value) || item.InstrumentName.ContainsIgnoreCase(value) ||
			item.Description.ContainsIgnoreCase(value);

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
			return DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException($"Invalid Twelve Data Unix timestamp '{value}'.", error);
		}
	}

	public static DateTime ParseCandleTime(string value, TimeSpan timeFrame, string timeZone)
	{
		var local = ParseNativeDateTime(value);
		if (timeFrame < TimeSpan.FromDays(1))
			return DateTime.SpecifyKind(local, DateTimeKind.Utc);
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
			FindTimeZone(timeZone));
	}

	public static DateTime GetCandleCloseTime(string value, DateTime openTime,
		TimeSpan timeFrame, string timeZone)
	{
		if (timeFrame < TimeSpan.FromDays(1))
			return openTime + timeFrame;
		var local = ParseNativeDateTime(value);
		var close = timeFrame == TimeSpan.FromDays(30) ? local.AddMonths(1) : local + timeFrame;
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(close, DateTimeKind.Unspecified),
			FindTimeZone(timeZone));
	}

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

	private static SecurityTypes ToSecurityType(this TwelveDataMarkets market, string nativeType)
		=> market switch
		{
			TwelveDataMarkets.Etfs => SecurityTypes.Etf,
			TwelveDataMarkets.Forex => SecurityTypes.Currency,
			TwelveDataMarkets.Crypto => SecurityTypes.CryptoCurrency,
			TwelveDataMarkets.Commodities => SecurityTypes.Commodity,
			_ when nativeType.ContainsIgnoreCase("preferred") => SecurityTypes.Stock,
			_ when nativeType.ContainsIgnoreCase("fund") => SecurityTypes.Fund,
			_ when nativeType.ContainsIgnoreCase("reit") => SecurityTypes.Stock,
			_ => SecurityTypes.Stock,
		};

	private static DateTime ParseNativeDateTime(string value)
	{
		var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
		if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var result))
		{
			throw new InvalidDataException($"Invalid Twelve Data candle timestamp '{value}'.");
		}
		return result;
	}

	private static TimeZoneInfo FindTimeZone(string id)
	{
		if (id.IsEmpty() || id.EqualsIgnoreCase("UTC"))
			return TimeZoneInfo.Utc;
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		}
		catch (TimeZoneNotFoundException error)
		{
			throw new InvalidDataException($"Unknown Twelve Data exchange time zone '{id}'.", error);
		}
	}

	private static string SanitizeIdentifier(string value)
		=> value.IsEmpty() || value.EqualsIgnoreCase("request_access_via_add_ons") ? null : value;

	private static bool IsCommoditySymbol(string symbol)
	{
		var code = symbol?.Split('/', 2)[0];
		return code is not null && (code.EqualsIgnoreCase("XAU") || code.EqualsIgnoreCase("XAG") ||
			code.EqualsIgnoreCase("XPT") || code.EqualsIgnoreCase("XPD") ||
			code.EqualsIgnoreCase("XBR") || code.EqualsIgnoreCase("XTI"));
	}
}

readonly record struct TwelveDataSecurityKey(TwelveDataMarkets Market, string Symbol,
	string Exchange, string MicCode)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture), Escape(Symbol),
			Escape(Exchange), Escape(MicCode));

	public static bool TryParse(string value, out TwelveDataSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 4 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(TwelveDataMarkets), marketValue))
		{
			return false;
		}
		var symbol = Unescape(parts[1]);
		if (symbol.IsEmpty())
			return false;
		key = new((TwelveDataMarkets)marketValue, symbol, Unescape(parts[2]), Unescape(parts[3]));
		return true;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}
