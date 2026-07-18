namespace StockSharp.FinancialModelingPrep;

static class Extensions
{
	public const string StockBoard = "FMP";
	public const string ForexBoard = "FMPFX";
	public const string CryptoBoard = "FMPCRYPTO";
	public const string IndexBoard = "FMPINDEX";
	public const string CommodityBoard = "FMPCOMMODITY";

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

	public static string ToBoard(this FmpMarkets market)
		=> market switch
		{
			FmpMarkets.Stocks => StockBoard,
			FmpMarkets.Forex => ForexBoard,
			FmpMarkets.Crypto => CryptoBoard,
			FmpMarkets.Indices => IndexBoard,
			FmpMarkets.Commodities => CommodityBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static FmpMarkets ToFmpMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(ForexBoard) ? FmpMarkets.Forex :
			boardCode.EqualsIgnoreCase(CryptoBoard) ? FmpMarkets.Crypto :
			boardCode.EqualsIgnoreCase(IndexBoard) ? FmpMarkets.Indices :
			boardCode.EqualsIgnoreCase(CommodityBoard) ? FmpMarkets.Commodities :
			FmpMarkets.Stocks;

	public static FmpSecurityKey GetFmpKey(this SecurityId securityId, string stockExchange)
	{
		var native = securityId.Native as string;
		if (FmpSecurityKey.TryParse(native, out var key))
			return key;

		var market = securityId.BoardCode.ToFmpMarket();
		var symbol = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new(market, NormalizeSymbol(symbol, market),
			market == FmpMarkets.Stocks ? stockExchange : null);
	}

	public static SecurityId NormalizeFmp(this SecurityId securityId, FmpSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this FmpSymbolItem item, FmpMarkets market,
		long originalTransactionId)
	{
		if (item?.Symbol.IsEmpty() != false)
			return null;
		var symbol = NormalizeSymbol(item.Symbol, market);
		var exchange = item.ExchangeShortName.IsEmpty(item.Exchange)
			.IsEmpty(item.StockExchange).IsEmpty(item.ExchangeFullName);
		var name = item.Name.IsEmpty(item.CompanyName);
		if (name.IsEmpty() && market == FmpMarkets.Forex)
			name = string.Join(" / ", new[] { item.FromName, item.ToName }
				.Where(value => !value.IsEmpty()));
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = market.ToBoard(),
				Native = new FmpSecurityKey(market, symbol, exchange).ToNative(),
			},
			Name = name.IsEmpty(symbol),
			ShortName = symbol,
			Class = exchange,
			SecurityType = item.ToSecurityType(market),
		};
		var currencyCode = item.Currency.IsEmpty(item.ToCurrency);
		if (Enum.TryParse<CurrencyTypes>(currencyCode, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static bool Matches(this FmpSymbolItem item, string value)
		=> value.IsEmpty() || item?.Symbol.ContainsIgnoreCase(value) == true ||
			item?.Name.ContainsIgnoreCase(value) == true ||
			item?.FromCurrency.ContainsIgnoreCase(value) == true ||
			item?.ToCurrency.ContainsIgnoreCase(value) == true ||
			item?.FromName.ContainsIgnoreCase(value) == true ||
			item?.ToName.ContainsIgnoreCase(value) == true;

	public static FmpMarkets GetMarket(this FmpSymbolItem item)
	{
		var exchange = item?.ExchangeShortName.IsEmpty(item?.Exchange)
			.IsEmpty(item?.ExchangeFullName);
		return exchange.EqualsIgnoreCase("FOREX") ? FmpMarkets.Forex :
			exchange.EqualsIgnoreCase("CRYPTO") || exchange.EqualsIgnoreCase("CCC")
				? FmpMarkets.Crypto :
			exchange.ContainsIgnoreCase("INDEX") ? FmpMarkets.Indices :
			exchange.ContainsIgnoreCase("COMMODITY") ? FmpMarkets.Commodities :
			FmpMarkets.Stocks;
	}

	public static bool IsTimeFrameSupported(this FmpMarkets market, TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(1) || timeFrame == TimeSpan.FromMinutes(1) ||
			timeFrame == TimeSpan.FromMinutes(5) || timeFrame == TimeSpan.FromHours(1))
		{
			return true;
		}
		return market == FmpMarkets.Stocks &&
			(timeFrame == TimeSpan.FromMinutes(15) ||
			 timeFrame == TimeSpan.FromMinutes(30) ||
			 timeFrame == TimeSpan.FromHours(4));
	}

	public static string ToFmpInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1min" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5min" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15min" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30min" :
			timeFrame == TimeSpan.FromHours(1) ? "1hour" :
			timeFrame == TimeSpan.FromHours(4) ? "4hour" :
			throw new NotSupportedException($"FMP intraday API does not support {timeFrame}.");

	public static DateTime GetCandleCloseTime(DateTime openTime, TimeSpan timeFrame)
		=> openTime + timeFrame;

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

	public static DateTime FromUnixTimestamp(long value)
	{
		try
		{
			return Math.Abs(value) >= 100_000_000_000L
				? DateTime.UnixEpoch.AddMilliseconds(value)
				: DateTime.UnixEpoch.AddSeconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException($"Invalid FMP Unix timestamp '{value}'.", error);
		}
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

	public static bool TryParseIntradayUtc(string value, TimeZoneInfo sourceTimeZone,
		out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || sourceTimeZone == null || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
		{
			return false;
		}
		parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
		try
		{
			result = TimeZoneInfo.ConvertTimeToUtc(parsed, sourceTimeZone);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	public static bool TryParseNewsTime(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || !DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
		{
			return false;
		}
		result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
		return true;
	}

	public static TimeZoneInfo ResolveTimeZone(string id)
	{
		id.ThrowIfEmpty(nameof(id));
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		}
		catch (TimeZoneNotFoundException error)
		{
			throw new InvalidOperationException($"FMP intraday time zone '{id}' was not found.",
				error);
		}
		catch (InvalidTimeZoneException error)
		{
			throw new InvalidOperationException($"FMP intraday time zone '{id}' is invalid.",
				error);
		}
	}

	public static string GetNewsSource(string url)
		=> Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

	public static string NormalizeSymbol(string symbol, FmpMarkets market)
	{
		symbol = symbol?.Trim().ToUpperInvariant();
		return market is FmpMarkets.Forex or FmpMarkets.Crypto
			? symbol?.Replace("/", string.Empty, StringComparison.Ordinal)
				.Replace("-", string.Empty, StringComparison.Ordinal)
				.Replace("_", string.Empty, StringComparison.Ordinal)
			: symbol;
	}

	public static bool IsUsStockStream(this FmpSecurityKey key)
	{
		if (key.Market != FmpMarkets.Stocks)
			return false;
		if (key.Exchange.IsEmpty())
			return true;
		return key.Exchange.ContainsIgnoreCase("NASDAQ") ||
			key.Exchange.ContainsIgnoreCase("NYSE") ||
			key.Exchange.ContainsIgnoreCase("AMEX") ||
			key.Exchange.ContainsIgnoreCase("CBOE") ||
			key.Exchange.ContainsIgnoreCase("OTC") ||
			key.Exchange.EqualsIgnoreCase("US");
	}

	public static string ToStreamSymbol(this FmpSecurityKey key)
		=> NormalizeSymbol(key.Symbol, key.Market)?.ToLowerInvariant();

	private static SecurityTypes ToSecurityType(this FmpSymbolItem item, FmpMarkets market)
	{
		if (market == FmpMarkets.Forex)
			return SecurityTypes.Currency;
		if (market == FmpMarkets.Crypto)
			return SecurityTypes.CryptoCurrency;
		if (market == FmpMarkets.Indices)
			return SecurityTypes.Index;
		if (market == FmpMarkets.Commodities)
			return SecurityTypes.Commodity;
		if (item.IsEtf == true || item.Type.ContainsIgnoreCase("etf"))
			return SecurityTypes.Etf;
		if (item.IsFund == true || item.Type.ContainsIgnoreCase("fund"))
			return SecurityTypes.Fund;
		return item.Type.ContainsIgnoreCase("index") ? SecurityTypes.Index :
			SecurityTypes.Stock;
	}
}

readonly record struct FmpSecurityKey(FmpMarkets Market, string Symbol, string Exchange)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture),
			Escape(Symbol), Escape(Exchange));

	public static bool TryParse(string value, out FmpSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(FmpMarkets), marketValue))
		{
			return false;
		}
		var symbol = Unescape(parts[1]);
		if (symbol.IsEmpty())
			return false;
		key = new((FmpMarkets)marketValue, symbol, Unescape(parts[2]));
		return true;
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Unescape(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}
