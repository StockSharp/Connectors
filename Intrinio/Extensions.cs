namespace StockSharp.Intrinio;

readonly record struct IntrinioOptionKey(
	string Code,
	string StreamCode,
	string Root,
	DateTime Expiry,
	decimal Strike,
	OptionTypes OptionType)
{
	public static bool TryParse(string value, out IntrinioOptionKey key)
	{
		key = default;
		value = value?.Replace('\u00a0', ' ').Trim().ToUpperInvariant();
		if (value.IsEmpty())
			return false;

		if (value.Length == 21)
		{
			var paddedRoot = value[..6];
			if (paddedRoot.Contains('_'))
				value = paddedRoot.TrimEnd('_') + value[6..];
			else if (paddedRoot.Contains(' '))
				value = paddedRoot.TrimEnd() + value[6..];
		}
		if (value.Length <= 15)
			return false;

		var rootLength = value.Length - 15;
		if (rootLength is < 1 or > 6)
			return false;
		var root = value[..rootLength];
		if (root.Any(character => !char.IsLetterOrDigit(character) && character != '.' && character != '-'))
			return false;
		if (!DateTime.TryParseExact(value.Substring(rootLength, 6), "yyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
		{
			return false;
		}

		var optionType = value[rootLength + 6] switch
		{
			'C' => OptionTypes.Call,
			'P' => OptionTypes.Put,
			_ => (OptionTypes?)null,
		};
		if (optionType == null || !long.TryParse(value.AsSpan(rootLength + 7, 8),
			NumberStyles.None, CultureInfo.InvariantCulture, out var strike))
		{
			return false;
		}

		var suffix = value[rootLength..];
		key = new(
			root + suffix,
			root.PadRight(6, '_') + suffix,
			root,
			DateTime.SpecifyKind(expiry.Date, DateTimeKind.Utc),
			strike / 1000m,
			optionType.Value);
		return true;
	}
}

static class Extensions
{
	public static EquityProvider ToSdk(this IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime => EquityProvider.REALTIME,
			IntrinioEquityProviders.DelayedSip => EquityProvider.DELAYED_SIP,
			IntrinioEquityProviders.NasdaqBasic => EquityProvider.NASDAQ_BASIC,
			IntrinioEquityProviders.CboeOne => EquityProvider.CBOE_ONE,
			IntrinioEquityProviders.Iex => EquityProvider.IEX,
			IntrinioEquityProviders.EquitiesEdge => EquityProvider.EQUITIES_EDGE,
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static OptionProvider ToSdk(this IntrinioOptionProviders provider)
		=> provider switch
		{
			IntrinioOptionProviders.Opra => OptionProvider.OPRA,
			IntrinioOptionProviders.OptionsEdge => OptionProvider.OPTIONS_EDGE,
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static string ToRealtimeSource(this IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime or IntrinioEquityProviders.Iex => "iex",
			IntrinioEquityProviders.DelayedSip => "delayed_sip",
			IntrinioEquityProviders.NasdaqBasic => "nasdaq_basic",
			IntrinioEquityProviders.CboeOne => "cboe_one",
			IntrinioEquityProviders.EquitiesEdge => "equities_edge",
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static string ToTradeSource(this IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime or IntrinioEquityProviders.Iex => "iex",
			IntrinioEquityProviders.DelayedSip => "delayed_sip",
			IntrinioEquityProviders.NasdaqBasic => "nasdaq_basic",
			IntrinioEquityProviders.CboeOne => "cboe_one_delayed",
			IntrinioEquityProviders.EquitiesEdge => throw new NotSupportedException(
				"Intrinio does not publish Equities Edge in the REST trade-history source list."),
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static string ToIntervalSource(this IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime or IntrinioEquityProviders.Iex => "realtime",
			IntrinioEquityProviders.DelayedSip => "delayed",
			IntrinioEquityProviders.NasdaqBasic => "nasdaq_basic",
			IntrinioEquityProviders.CboeOne => "cboe_one",
			IntrinioEquityProviders.EquitiesEdge => "equities_edge",
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static string ToQuoteSource(this IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime or IntrinioEquityProviders.Iex => "iex",
			IntrinioEquityProviders.DelayedSip => "delayed_sip",
			IntrinioEquityProviders.NasdaqBasic => "nasdaq_basic",
			IntrinioEquityProviders.CboeOne => "cboe_one_delayed",
			IntrinioEquityProviders.EquitiesEdge => null,
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	public static string ToOptionRestSource(this bool isDelayed)
		=> isDelayed ? "delayed" : "realtime";

	public static string ToIntrinioInterval(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => "1m",
			{ TotalMinutes: 5 } => "5m",
			{ TotalMinutes: 10 } => "10m",
			{ TotalMinutes: 15 } => "15m",
			{ TotalMinutes: 30 } => "30m",
			{ TotalMinutes: 60 } => "1h",
			_ => throw new NotSupportedException(
				$"Intrinio does not support the {timeFrame} intraday interval."),
		};

	public static string ToIntrinioFrequency(this TimeSpan timeFrame)
		=> timeFrame.TotalDays switch
		{
			1 => "daily",
			7 => "weekly",
			30 => "monthly",
			90 => "quarterly",
			365 => "yearly",
			_ => throw new NotSupportedException(
				$"Intrinio does not support the {timeFrame} end-of-day frequency."),
		};

	public static bool IsIntraday(this TimeSpan timeFrame)
		=> timeFrame < TimeSpan.FromDays(1);

	public static SecurityMessage ToSecurityMessage(this IntrinioSecuritySummary security,
		long originalTransactionId)
	{
		var ticker = security.Ticker.IsEmpty(security.CompositeTicker);
		var securityId = new SecurityId
		{
			SecurityCode = ticker,
			BoardCode = IntrinioMessageAdapter.EquityBoard,
			Native = security.Id.IsEmpty(ticker),
			Bloomberg = security.Figi,
		};
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = security.Name.IsEmpty(ticker),
			ShortName = security.Name.IsEmpty(ticker),
			SecurityType = security.Code.ToSecurityType(),
			Class = security.ExchangeMic.IsEmpty(security.Exchange),
		};
		if (Enum.TryParse<CurrencyTypes>(security.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityMessage ToSecurityMessage(this IntrinioOption option,
		long originalTransactionId)
	{
		if (!IntrinioOptionKey.TryParse(option.Code, out var key))
			throw new InvalidOperationException(
				$"Intrinio returned invalid option code '{option.Code}'.");
		var expiry = key.Expiry;
		if (!option.Expiration.IsEmpty() && DateTime.TryParseExact(option.Expiration,
			"yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			expiry = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
		}
		return new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = key.Code,
				BoardCode = IntrinioMessageAdapter.OptionBoard,
				Native = option.Id.IsEmpty(option.Code),
			},
			Name = key.Code,
			ShortName = key.Code,
			SecurityType = SecurityTypes.Option,
			Currency = CurrencyTypes.USD,
			ExpiryDate = expiry,
			Strike = option.Strike ?? key.Strike,
			OptionType = option.Type switch
			{
				"call" => OptionTypes.Call,
				"put" => OptionTypes.Put,
				_ => key.OptionType,
			},
			Multiplier = 100m,
			UnderlyingSecurityId = new()
			{
				SecurityCode = option.Ticker.IsEmpty(key.Root),
				BoardCode = IntrinioMessageAdapter.EquityBoard,
				Native = option.Ticker.IsEmpty(key.Root),
			},
		};
	}

	public static SecurityTypes ToSecurityType(this string code)
		=> code?.ToUpperInvariant() switch
		{
			"ETF" or "ETC" => SecurityTypes.Etf,
			"IDX" => SecurityTypes.Index,
			"FND" or "MF" or "IF" or "UIT" => SecurityTypes.Fund,
			"CUR" => SecurityTypes.Currency,
			"COM" => SecurityTypes.Commodity,
			"DR" or "CDR" => SecurityTypes.Adr,
			"GDR" or "GDN" => SecurityTypes.Gdr,
			"WAR" or "CW" => SecurityTypes.Warrant,
			"NTS" or "DEB" or "NCD" or "CN" or "ETN" => SecurityTypes.Bond,
			_ => SecurityTypes.Stock,
		};

	public static SecurityId NormalizeIntrinio(this SecurityId securityId,
		string code, bool isOption, string native)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(code);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(
			isOption ? IntrinioMessageAdapter.OptionBoard : IntrinioMessageAdapter.EquityBoard);
		securityId.Native ??= native;
		return securityId;
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static decimal ToIntrinioTime(this DateTime value)
		=> value.TimeOfDay.Ticks / (decimal)TimeSpan.TicksPerSecond;

	public static DateTime ToUtcFromUnixSeconds(this double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
			throw new FormatException($"Invalid Intrinio Unix timestamp '{value}'.");
		try
		{
			var ticks = checked((long)decimal.Round(
				Convert.ToDecimal(value) * TimeSpan.TicksPerSecond,
				0, MidpointRounding.AwayFromZero));
			return DateTime.UnixEpoch.AddTicks(ticks);
		}
		catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
		{
			throw new FormatException($"Invalid Intrinio Unix timestamp '{value}'.", error);
		}
	}

	public static decimal? ToSafeDecimal(this double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
			return null;
		try
		{
			return checked((decimal)value);
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	public static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 10000) : 500;
		try
		{
			return to - TimeSpan.FromTicks(checked(timeFrame.Ticks * bars));
		}
		catch (OverflowException)
		{
			return DateTime.UnixEpoch;
		}
	}
}
