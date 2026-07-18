namespace StockSharp.Exegy;

static class ExegyExtensions
{
	public const string BoardCode = "EXEGY";

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

	public static SecurityId ToSecurityId(this ExegySecurityKey key)
		=> new()
		{
			SecurityCode = key.SecurityCode,
			BoardCode = key.ToBoard(),
			Native = key.ToNative(),
		};

	public static ExegySecurityKey GetExegyKey(this SecurityId securityId)
	{
		if (ExegySecurityKey.TryParse(securityId.Native as string, out var key))
			return key;
		var code = (securityId.Native as string).IsEmpty(securityId.SecurityCode)?.Trim();
		code.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var venue = securityId.BoardCode.EqualsIgnoreCase(BoardCode)
			? null : securityId.BoardCode;
		return new(null, code, venue);
	}

	public static SecurityId NormalizeExegy(this SecurityId securityId, ExegySecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.SecurityCode);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static string ToBoard(this ExegySecurityKey key)
	{
		if (key.Venue.IsEmpty())
			return BoardCode;
		var board = new string(key.Venue.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
		return board.IsEmpty(BoardCode);
	}

	public static SecurityMessage ToSecurityMessage(this ExegyReferenceRow row,
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
			Class = row.SecurityType,
			SecurityType = ToSecurityType(row.SecurityType),
			Currency = Enum.TryParse<CurrencyTypes>(row.Currency, true, out var currency)
				? currency : null,
			ExpiryDate = row.Expiration,
			Strike = row.Strike,
			OptionType = ToOptionType(row.OptionType),
			PriceStep = Positive(row.PriceStep),
			Multiplier = Positive(row.Multiplier),
		};
	}

	public static SecurityMessage ToSecurityMessage(this ExegyMarketRow row,
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

	public static Sides? ToSide(this ExegyMarketRow row)
	{
		if (row.IsBid)
			return Sides.Buy;
		if (row.IsAsk)
			return Sides.Sell;
		return null;
	}

	public static OrderStates ToOrderState(this ExegyMarketRow row)
		=> row.IsCancellation || row.IsReset ||
			!row.OrderId.IsEmpty() && row.Size is <= 0
			? OrderStates.Done : OrderStates.Active;

	public static DateTime ToUtc(DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static bool InRange(DateTime time, MarketDataMessage message)
		=> (message.From == null || time >= ToUtc(message.From.Value)) &&
			(message.To == null || time <= ToUtc(message.To.Value));

	public static decimal? Positive(decimal? value) => value is > 0 ? value : null;
	public static decimal? NonNegative(decimal? value) => value is >= 0 ? value : null;
	public static int? NonNegative(int? value) => value is >= 0 ? value : null;

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
