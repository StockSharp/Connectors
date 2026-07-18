namespace StockSharp.CboeDataShop;

readonly record struct CboeOptionKey(
	string Code,
	string Root,
	DateTime Expiry,
	decimal Strike,
	OptionTypes OptionType)
{
	public static bool TryParse(string value, out CboeOptionKey key)
	{
		key = default;
		value = value?.Trim();
		if (value.IsEmpty() || value.Length <= 15)
			return false;

		var tail = value.Length - 15;
		var root = value[..tail].Trim();
		if (root.IsEmpty() || !DateTime.TryParseExact(value.Substring(tail, 6), "yyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
		{
			return false;
		}

		var optionType = char.ToUpperInvariant(value[tail + 6]) switch
		{
			'C' => OptionTypes.Call,
			'P' => OptionTypes.Put,
			_ => (OptionTypes?)null,
		};
		if (optionType == null || !long.TryParse(value.AsSpan(tail + 7, 8),
			NumberStyles.None, CultureInfo.InvariantCulture, out var strike))
		{
			return false;
		}

		var code = root + value[tail..];
		key = new(code, root, expiry.Date, strike / 1000m, optionType.Value);
		return true;
	}
}

static class Extensions
{
	private static readonly TimeZoneInfo _eastern = FindTimeZone(
		"Eastern Standard Time", "America/New_York", TimeSpan.FromHours(-5));

	public static CboeOptionTypes ToNative(this OptionTypes optionType)
		=> optionType switch
		{
			OptionTypes.Call => CboeOptionTypes.Call,
			OptionTypes.Put => CboeOptionTypes.Put,
			_ => throw new ArgumentOutOfRangeException(nameof(optionType), optionType, null),
		};

	public static string ToApi(this CboeOptionTypes optionType)
		=> optionType switch
		{
			CboeOptionTypes.Call => "C",
			CboeOptionTypes.Put => "P",
			_ => throw new ArgumentOutOfRangeException(nameof(optionType), optionType, null),
		};

	public static OptionTypes? ToOptionType(this CboeOptionTypes? value)
		=> value switch
		{
			CboeOptionTypes.Call => OptionTypes.Call,
			CboeOptionTypes.Put => OptionTypes.Put,
			_ => null,
		};

	public static DateTime GetEasternDate(DateTime utc)
	{
		utc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
		return TimeZoneInfo.ConvertTimeFromUtc(utc, _eastern).Date;
	}

	public static string ToEasternTime(DateTime utc)
	{
		utc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
		return TimeZoneInfo.ConvertTimeFromUtc(utc, _eastern)
			.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
	}

	public static DateTime ToServerTime(this DateTime date, string timestamp)
	{
		if (!TimeSpan.TryParse(timestamp, CultureInfo.InvariantCulture, out var time) ||
			time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
		{
			throw new FormatException($"Invalid Cboe Eastern timestamp '{timestamp}'.");
		}
		var local = DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Unspecified);
		var utc = TimeZoneInfo.ConvertTimeToUtc(local, _eastern);
		return utc;
	}

	public static DateTime ToUtcDate(this DateTime date)
		=> DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

	public static SecurityMessage ToSecurityMessage(this CboeSymbol symbol,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = symbol.Name,
				BoardCode = "CBOE",
				Native = symbol.Name,
			},
			Name = symbol.CompanyName.IsEmpty(symbol.Name),
			ShortName = symbol.CompanyName.IsEmpty(symbol.Name),
			SecurityType = SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
		};

	public static SecurityMessage ToSecurityMessage(this CboeOptionQuote option,
		long originalTransactionId)
	{
		DateTime? expiry = null;
		if (DateTime.TryParseExact(option.Expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var parsed))
		{
			expiry = parsed.Date;
		}

		return new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = option.Option,
				BoardCode = "CBOEOPT",
				Native = option.Option,
			},
			Name = option.Option,
			ShortName = option.Option,
			SecurityType = SecurityTypes.Option,
			Currency = CurrencyTypes.USD,
			ExpiryDate = expiry,
			Strike = option.Strike,
			OptionType = option.OptionType.ToOptionType(),
			Multiplier = 100m,
			UnderlyingSecurityId = new()
			{
				SecurityCode = option.Root,
				BoardCode = "CBOE",
				Native = option.Root,
			},
		};
	}

	public static SecurityId Normalize(this SecurityId securityId, bool isOption, string nativeCode)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(nativeCode);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(isOption ? "CBOEOPT" : "CBOE");
		securityId.Native ??= nativeCode;
		return securityId;
	}

	public static Sides? ToOriginSide(this CboeTradeLocations? value)
		=> value switch
		{
			CboeTradeLocations.OnAsk or CboeTradeLocations.AboveAsk => Sides.Buy,
			CboeTradeLocations.OnBid or CboeTradeLocations.BelowBid => Sides.Sell,
			_ => null,
		};

	private static TimeZoneInfo FindTimeZone(string windowsId, string ianaId, TimeSpan fallback)
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
		}
		catch (TimeZoneNotFoundException)
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
			}
			catch (TimeZoneNotFoundException)
			{
				return TimeZoneInfo.CreateCustomTimeZone(windowsId, fallback, windowsId, windowsId);
			}
		}
	}
}
