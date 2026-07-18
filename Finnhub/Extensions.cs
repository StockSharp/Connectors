namespace StockSharp.Finnhub;

static class Extensions
{
	public const string StockBoard = "FINNHUB";
	public const string ForexBoard = "FINNHUBFX";
	public const string CryptoBoard = "FINNHUBCRYPTO";

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

	public static string ToNative(this FinnhubNewsCategories category)
		=> category switch
		{
			FinnhubNewsCategories.General => "general",
			FinnhubNewsCategories.Forex => "forex",
			FinnhubNewsCategories.Crypto => "crypto",
			FinnhubNewsCategories.Mergers => "merger",
			_ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
		};

	public static string ToNativeResolution(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30" :
			timeFrame == TimeSpan.FromHours(1) ? "60" :
			timeFrame == TimeSpan.FromDays(1) ? "D" :
			timeFrame == TimeSpan.FromDays(7) ? "W" :
			timeFrame == TimeSpan.FromDays(30) ? "M" :
			throw new NotSupportedException($"Finnhub does not support {timeFrame} candles.");

	public static string ToBoard(this FinnhubMarkets market)
		=> market switch
		{
			FinnhubMarkets.Stocks => StockBoard,
			FinnhubMarkets.Forex => ForexBoard,
			FinnhubMarkets.Crypto => CryptoBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static FinnhubMarkets GetFinnhubMarket(this SecurityId securityId,
		string forexExchange, string cryptoExchange)
	{
		if (securityId.BoardCode.EqualsIgnoreCase(ForexBoard))
			return FinnhubMarkets.Forex;
		if (securityId.BoardCode.EqualsIgnoreCase(CryptoBoard))
			return FinnhubMarkets.Crypto;
		if (securityId.BoardCode.EqualsIgnoreCase(StockBoard))
			return FinnhubMarkets.Stocks;

		var symbol = (securityId.Native as string).IsEmpty(securityId.SecurityCode);
		var prefix = symbol?.Split(':', 2)[0];
		if (prefix.EqualsIgnoreCase(forexExchange))
			return FinnhubMarkets.Forex;
		if (prefix.EqualsIgnoreCase(cryptoExchange))
			return FinnhubMarkets.Crypto;
		return FinnhubMarkets.Stocks;
	}

	public static string GetFinnhubSymbol(this SecurityId securityId)
		=> (securityId.Native as string).IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));

	public static SecurityId NormalizeFinnhub(this SecurityId securityId, FinnhubMarkets market,
		string symbol)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(market.ToBoard());
		securityId.Native ??= symbol;
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this FinnhubStockSymbol symbol,
		long originalTransactionId)
	{
		var native = symbol.Symbol.IsEmpty(symbol.DisplaySymbol);
		var code = symbol.DisplaySymbol.IsEmpty(native);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = StockBoard,
				Native = native,
				Bloomberg = symbol.Figi,
				Isin = symbol.Isin,
			},
			Name = symbol.Description.IsEmpty(code),
			ShortName = code,
			Class = symbol.Mic,
			SecurityType = symbol.Type.ToFinnhubSecurityType(),
		};
		if (Enum.TryParse<CurrencyTypes>(symbol.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityMessage ToSecurityMessage(this FinnhubSymbolLookupItem symbol,
		long originalTransactionId)
	{
		var native = symbol.Symbol.IsEmpty(symbol.DisplaySymbol);
		var code = symbol.DisplaySymbol.IsEmpty(native);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = StockBoard,
				Native = native,
			},
			Name = symbol.Description.IsEmpty(code),
			ShortName = code,
			SecurityType = symbol.Type.ToFinnhubSecurityType(),
		};
	}

	public static SecurityMessage ToSecurityMessage(this FinnhubAssetSymbol symbol,
		FinnhubMarkets market, string exchange, long originalTransactionId)
	{
		if (market == FinnhubMarkets.Stocks)
			throw new ArgumentOutOfRangeException(nameof(market), market, null);
		var native = symbol.Symbol.IsEmpty(symbol.DisplaySymbol);
		var code = symbol.DisplaySymbol.IsEmpty(native);
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = market.ToBoard(),
				Native = native,
			},
			Name = symbol.Description.IsEmpty(code),
			ShortName = code,
			Class = exchange,
			SecurityType = market == FinnhubMarkets.Forex
				? SecurityTypes.Currency : SecurityTypes.CryptoCurrency,
		};

		var pair = code?.Split(['/', '_'], StringSplitOptions.RemoveEmptyEntries);
		if (pair?.Length >= 2 && Enum.TryParse<CurrencyTypes>(pair[^1], true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityTypes? ToFinnhubSecurityType(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (value.ContainsIgnoreCase("etf") || value.ContainsIgnoreCase("etp") ||
			value.ContainsIgnoreCase("exchange traded"))
			return SecurityTypes.Etf;
		if (value.ContainsIgnoreCase("adr") || value.ContainsIgnoreCase("depositary receipt"))
			return SecurityTypes.Adr;
		if (value.ContainsIgnoreCase("gdr"))
			return SecurityTypes.Gdr;
		if (value.ContainsIgnoreCase("fund") || value.ContainsIgnoreCase("unit trust"))
			return SecurityTypes.Fund;
		if (value.ContainsIgnoreCase("index"))
			return SecurityTypes.Index;
		if (value.ContainsIgnoreCase("bond") || value.ContainsIgnoreCase("note") ||
			value.ContainsIgnoreCase("debt"))
			return SecurityTypes.Bond;
		if (value.ContainsIgnoreCase("warrant"))
			return SecurityTypes.Warrant;
		if (value.ContainsIgnoreCase("preferred") || value.ContainsIgnoreCase("common stock") ||
			value.ContainsIgnoreCase("equity") || value.ContainsIgnoreCase("reit"))
			return SecurityTypes.Stock;
		return SecurityTypes.Stock;
	}

	public static bool Matches(this FinnhubAssetSymbol symbol, string value)
		=> value.IsEmpty() || symbol.Symbol.ContainsIgnoreCase(value) ||
			symbol.DisplaySymbol.ContainsIgnoreCase(value) || symbol.Description.ContainsIgnoreCase(value);

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
			throw new InvalidDataException($"Invalid Finnhub Unix timestamp '{value}'.", error);
		}
	}

	public static DateTime FromUnixMilliseconds(long value)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException($"Invalid Finnhub Unix timestamp '{value}'.", error);
		}
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

	public static DateTime GetCandleCloseTime(DateTime openTime, TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(30) ? openTime.AddMonths(1) : openTime + timeFrame;
}
