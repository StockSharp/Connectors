namespace StockSharp.QuantFeed;

static class QuantFeedExtensions
{
	public const string BoardCode = "QUANTFEED";

	public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
	{
		timeZoneId.ThrowIfEmpty(nameof(timeZoneId));
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
		}
		catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			throw new ArgumentException($"Time zone '{timeZoneId}' is not available on this system.",
				nameof(timeZoneId), ex);
		}
	}

	public static SecurityId ToSecurityId(this QuantFeedSecurityKey key)
		=> new()
		{
			SecurityCode = key.SecurityCode,
			BoardCode = key.Mic.IsEmpty(BoardCode),
			Native = key.ToNative(),
		};

	public static QuantFeedSecurityKey GetQuantFeedKey(this SecurityId securityId)
	{
		if (QuantFeedSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;

		var code = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		code.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var mic = securityId.BoardCode.EqualsIgnoreCase(BoardCode)
			? null : securityId.BoardCode;
		return new(null, code, mic);
	}

	public static SecurityId NormalizeQuantFeed(this SecurityId securityId,
		QuantFeedSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.SecurityCode);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Mic.IsEmpty(BoardCode));
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this QuantFeedReferenceRow row,
		long originalTransactionId)
	{
		var key = row.ToKey();
		var securityId = key.ToSecurityId();
		securityId.Isin = row.Isin;
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = row.Name.IsEmpty(key.SecurityCode),
			ShortName = key.SecurityCode,
			SecurityType = ToSecurityType(row.SecurityType),
			Currency = ToCurrency(row.Currency),
			ExpiryDate = row.Expiration,
			Strike = row.Strike,
			OptionType = ToOptionType(row.OptionType),
			PriceStep = Positive(row.PriceStep),
			Multiplier = Positive(row.Multiplier),
		};
	}

	public static SecurityMessage ToSecurityMessage(this QuantFeedMarketRow row,
		long originalTransactionId)
	{
		var key = row.ToKey();
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = key.ToSecurityId(),
			Name = key.SecurityCode,
			ShortName = key.SecurityCode,
		};
	}

	public static SecurityTypes? ToSecurityType(string value)
	{
		if (value.IsEmpty())
			return null;
		var normalized = Normalize(value);
		return normalized switch
		{
			"stock" or "share" or "equity" => SecurityTypes.Stock,
			"etf" or "exchangetradedfund" => SecurityTypes.Etf,
			"fund" or "mutualfund" => SecurityTypes.Fund,
			"future" or "futures" => SecurityTypes.Future,
			"option" or "options" => SecurityTypes.Option,
			"index" => SecurityTypes.Index,
			"bond" or "fixedincome" => SecurityTypes.Bond,
			"currency" or "forex" or "fx" => SecurityTypes.Currency,
			"commodity" => SecurityTypes.Commodity,
			"warrant" => SecurityTypes.Warrant,
			"cfd" => SecurityTypes.Cfd,
			"adr" => SecurityTypes.Adr,
			"gdr" => SecurityTypes.Gdr,
			_ => Enum.TryParse<SecurityTypes>(value, true, out var type) ? type : null,
		};
	}

	public static OptionTypes? ToOptionType(string value)
	{
		if (value.IsEmpty())
			return null;
		return value.EqualsIgnoreCase("C") || value.EqualsIgnoreCase("CALL")
			? OptionTypes.Call
			: value.EqualsIgnoreCase("P") || value.EqualsIgnoreCase("PUT")
				? OptionTypes.Put : null;
	}

	public static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static SecurityStates? ToSecurityState(string value)
	{
		if (value.IsEmpty())
			return null;
		var normalized = Normalize(value);
		if (normalized.Contains("halt", StringComparison.Ordinal) ||
			normalized.Contains("suspend", StringComparison.Ordinal) ||
			normalized is "closed" or "close" or "stopped" or "nottrading")
		{
			return SecurityStates.Stoped;
		}
		if (normalized.Contains("trad", StringComparison.Ordinal) ||
			normalized is "open" or "continuous")
		{
			return SecurityStates.Trading;
		}
		return null;
	}

	public static bool InRange(DateTime time, MarketDataMessage message)
		=> (message.From == null || time >= ToUtc(message.From.Value)) &&
			(message.To == null || time <= ToUtc(message.To.Value));

	public static DateTime ToUtc(DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	public static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	public static int? NonNegative(int? value)
		=> value is >= 0 ? value : null;

	private static string Normalize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value)
		{
			if (char.IsLetterOrDigit(character))
				builder.Append(char.ToLowerInvariant(character));
		}
		return builder.ToString();
	}
}
