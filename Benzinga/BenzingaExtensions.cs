namespace StockSharp.Benzinga;

static class BenzingaExtensions
{
	public const string BoardCode = "BZNG";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static BenzingaSecurityKey GetBenzingaKey(this SecurityId securityId)
	{
		if (BenzingaSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;

		var symbol = (securityId.Native as string).IsEmpty(securityId.SecurityCode)?.Trim();
		symbol.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var board = securityId.BoardCode;
		if (board.EqualsIgnoreCase(BoardCode))
			board = null;
		return new(symbol.ToUpperInvariant(), ToBoardCode(board));
	}

	public static SecurityId NormalizeBenzinga(this SecurityId securityId,
		BenzingaSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Exchange.IsEmpty(BoardCode));
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this BenzingaDelayedQuote quote,
		long originalTransactionId, SecurityId requested)
	{
		if (quote?.Symbol.IsEmpty() != false)
			return null;

		var board = ToBoardCode(quote.IsoExchange.IsEmpty(quote.Exchange)
			.IsEmpty(quote.BenzingaExchange));
		var key = new BenzingaSecurityKey(quote.Symbol.ToUpperInvariant(), board);
		var securityId = key.ToSecurityId();
		securityId.Isin = quote.Isin.IsEmpty(requested.Isin);
		securityId.Cusip = quote.Cusip.IsEmpty(requested.Cusip);

		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = quote.CompanyStandardName.IsEmpty(quote.IssuerName)
				.IsEmpty(quote.Description).IsEmpty(quote.Name).IsEmpty(quote.Symbol),
			ShortName = quote.IssuerShortName.IsEmpty(quote.Name).IsEmpty(quote.Symbol),
			Class = quote.Industry.IsEmpty(quote.Sector),
			SecurityType = quote.Type.ToSecurityType(),
			Currency = quote.Currency.ToCurrency(),
			VolumeStep = 1,
		};
	}

	public static SecurityId ToSecurityId(this BenzingaSecurityKey key)
		=> new()
		{
			SecurityCode = key.Symbol,
			BoardCode = key.Exchange.IsEmpty(BoardCode),
			Native = key.ToNative(),
		};

	public static string ToInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5m" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15m" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30m" :
			timeFrame == TimeSpan.FromHours(1) ? "1h" :
			timeFrame == TimeSpan.FromDays(1) ? "1d" :
			timeFrame == TimeSpan.FromDays(7) ? "1w" :
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Benzinga candle time frame.");

	public static string ToNative(this BenzingaSessions session)
		=> session switch
		{
			BenzingaSessions.Any => "ANY",
			BenzingaSessions.PreMarket => "PRE_MARKET",
			BenzingaSessions.Regular => "REGULAR",
			BenzingaSessions.AfterMarket => "AFTER_MARKET",
			_ => throw new ArgumentOutOfRangeException(nameof(session), session, null),
		};

	public static DateTime? FromUnixMilliseconds(long? value)
	{
		if (value == null)
			return null;
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
			out var parsed))
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

	public static string ToBoardCode(string value)
	{
		if (value.IsEmpty())
			return BoardCode;
		var result = new string(value.Where(char.IsLetterOrDigit).ToArray())
			.ToUpperInvariant();
		return result.IsEmpty(BoardCode);
	}

	public static HashSet<string> ParseChannels(string value)
		=> value.IsEmpty() ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries).Where(channel => !channel.IsEmpty())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	public static bool Matches(this BenzingaNewsItem item, string symbol,
		HashSet<string> channels)
	{
		if (item == null)
			return false;
		if (!symbol.IsEmpty() && !(item.Stocks ?? [])
			.Any(stock => stock?.Name.EqualsIgnoreCase(symbol) == true))
		{
			return false;
		}
		return channels.Count == 0 || (item.Channels ?? [])
			.Any(channel => channel?.Name != null && channels.Contains(channel.Name));
	}

	public static SecurityId GetNewsSecurityId(this BenzingaNewsItem item,
		SecurityId requested)
	{
		if (!requested.SecurityCode.IsEmpty())
			return requested;
		var stock = item?.Stocks?.FirstOrDefault(value => value?.Name.IsEmpty() == false);
		if (stock == null)
			return default;
		var key = new BenzingaSecurityKey(stock.Name.ToUpperInvariant(),
			ToBoardCode(stock.Exchange));
		var securityId = key.ToSecurityId();
		securityId.Isin = stock.Isin;
		securityId.Cusip = stock.Cusip;
		return securityId;
	}

	public static decimal? Positive(decimal? value) => value is > 0 ? value : null;
	public static decimal? NonNegative(decimal? value) => value is >= 0 ? value : null;

	private static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	private static SecurityTypes? ToSecurityType(this string value)
	{
		if (value.IsEmpty())
			return null;
		var normalized = new string(value.Where(char.IsLetterOrDigit)
			.Select(char.ToUpperInvariant).ToArray());
		return normalized switch
		{
			"STOCK" or "COMMONSTOCK" => SecurityTypes.Stock,
			"ETF" => SecurityTypes.Etf,
			"ADR" => SecurityTypes.Adr,
			"FUND" or "MUTUALFUND" => SecurityTypes.Fund,
			"INDEX" => SecurityTypes.Index,
			"OPTION" => SecurityTypes.Option,
			"FUTURE" => SecurityTypes.Future,
			"WARRANT" => SecurityTypes.Warrant,
			_ => null,
		};
	}
}

readonly record struct BenzingaSecurityKey(string Symbol, string Exchange)
{
	private const char _separator = '|';

	public string ToNative()
		=> $"{Exchange.IsEmpty(BenzingaExtensions.BoardCode)}{_separator}{Symbol}";

	public static bool TryParse(string value, out BenzingaSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var separator = value.IndexOf(_separator);
		if (separator <= 0 || separator == value.Length - 1 ||
			value.IndexOf(_separator, separator + 1) >= 0)
		{
			return false;
		}
		var exchange = BenzingaExtensions.ToBoardCode(value[..separator]);
		var symbol = value[(separator + 1)..].Trim();
		if (symbol.IsEmpty())
			return false;
		key = new(symbol.ToUpperInvariant(), exchange);
		return true;
	}
}
