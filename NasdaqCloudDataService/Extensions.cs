namespace StockSharp.NasdaqCloudDataService;

readonly record struct NasdaqCloudOptionKey(
	string Code,
	string ApiIdentifier,
	string Root,
	DateTime Expiry,
	decimal Strike,
	OptionTypes OptionType)
{
	public static bool TryParse(string value, out NasdaqCloudOptionKey key)
	{
		key = default;
		value = value?.Replace('\u00a0', ' ').Trim();
		if (value.IsEmpty() || value.Length <= 15)
			return false;

		var tail = value.Length - 15;
		var root = value[..tail].Trim();
		if (root.IsEmpty() || root.Length > 6 ||
			!DateTime.TryParseExact(value.Substring(tail, 6), "yyMMdd",
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

		var suffix = value[tail..].ToUpperInvariant();
		key = new(
			root.ToUpperInvariant() + suffix,
			root.ToUpperInvariant().PadRight(6) + suffix,
			root.ToUpperInvariant(),
			DateTime.SpecifyKind(expiry.Date, DateTimeKind.Utc),
			strike / 1000m,
			optionType.Value);
		return true;
	}
}

static class Extensions
{
	private static readonly TimeZoneInfo _eastern = FindTimeZone(
		"Eastern Standard Time", "America/New_York", TimeSpan.FromHours(-5));
	private static readonly string[] _timestampFormats =
		["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm"];

	public static string ToApi(this NasdaqCloudSources source)
		=> source switch
		{
			NasdaqCloudSources.Nasdaq => "nasdaq",
			NasdaqCloudSources.Bx => "bx",
			NasdaqCloudSources.Psx => "psx",
			NasdaqCloudSources.Cqt => "cqt",
			_ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
		};

	public static string ToApi(this NasdaqCloudOffsets offset)
		=> offset switch
		{
			NasdaqCloudOffsets.Realtime => "realtime",
			NasdaqCloudOffsets.Delayed => "delayed",
			_ => throw new ArgumentOutOfRangeException(nameof(offset), offset, null),
		};

	public static string ToApi(this NasdaqCloudBarRanges range)
		=> range switch
		{
			NasdaqCloudBarRanges.Day => "1d",
			NasdaqCloudBarRanges.FiveDays => "5d",
			NasdaqCloudBarRanges.Month => "1m",
			NasdaqCloudBarRanges.ThreeMonths => "3m",
			NasdaqCloudBarRanges.SixMonths => "6m",
			NasdaqCloudBarRanges.Year => "1y",
			NasdaqCloudBarRanges.FiveYears => "5y",
			NasdaqCloudBarRanges.Maximum => "max",
			NasdaqCloudBarRanges.YearToDate => "ytd",
			_ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
		};

	public static string ToNasdaqCloudPrecision(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => "1minute",
			{ TotalMinutes: 5 } => "5minute",
			{ TotalMinutes: 10 } => "10minute",
			{ TotalMinutes: 15 } => "15minute",
			{ TotalMinutes: 30 } => "30minute",
			{ TotalDays: 1 } => "1day",
			{ TotalDays: 7 } => "1week",
			{ TotalDays: 30 } => "1month",
			_ => throw new NotSupportedException(
				$"Nasdaq Cloud does not support the {timeFrame} candle precision."),
		};

	public static DateTime? ToNasdaqCloudTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParseExact(value.Trim(), _timestampFormats,
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
		{
			throw new FormatException($"Invalid Nasdaq Cloud Eastern timestamp '{value}'.");
		}
		local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		return TimeZoneInfo.ConvertTimeToUtc(local, _eastern);
	}

	public static string ToEasternRoute(this DateTime value)
	{
		var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		return TimeZoneInfo.ConvertTimeFromUtc(utc, _eastern)
			.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
	}

	public static SecurityMessage ToSecurityMessage(this NasdaqCloudEquity equity,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = equity.Symbol,
				BoardCode = "NCDS",
				Native = equity.Symbol,
			},
			Name = equity.SecurityName.IsEmpty(equity.Symbol),
			ShortName = equity.SecurityName.IsEmpty(equity.Symbol),
			SecurityType = equity.IsEtf ? SecurityTypes.Etf : SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
			Class = equity.ListingExchange,
		};

	public static SecurityMessage ToSecurityMessage(this NasdaqCloudIndex index,
		long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = index.Instrument,
				BoardCode = "NCDSIDX",
				Native = index.Instrument,
			},
			Name = index.InstrumentName.IsEmpty(index.Instrument),
			ShortName = index.InstrumentName.IsEmpty(index.Instrument),
			SecurityType = SecurityTypes.Index,
			Class = new[] { index.AssetType, index.FinancialProductType }
				.Where(value => !value.IsEmpty()).Join(" / "),
		};
		if (Enum.TryParse<CurrencyTypes>(index.Currency, true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static SecurityMessage ToSecurityMessage(this NasdaqCloudEtp etp,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = etp.Symbol,
				BoardCode = "NCDSETP",
				Native = etp.Symbol,
			},
			Name = etp.Name.IsEmpty(etp.Symbol),
			ShortName = etp.Name.IsEmpty(etp.Symbol),
			SecurityType = SecurityTypes.Etf,
			Currency = CurrencyTypes.USD,
			Class = etp.FinancialProductType,
		};

	public static SecurityMessage ToSecurityMessage(this NasdaqCloudOptionContract contract,
		long originalTransactionId)
	{
		if (!NasdaqCloudOptionKey.TryParse(contract.Identifier, out var key))
			throw new InvalidOperationException(
				$"Nasdaq Cloud returned invalid OSI option identifier '{contract.Identifier}'.");

		return new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = key.Code,
				BoardCode = "NCDSOPT",
				Native = key.ApiIdentifier,
			},
			Name = key.Code,
			ShortName = key.Code,
			SecurityType = SecurityTypes.Option,
			Currency = CurrencyTypes.USD,
			ExpiryDate = contract.Expiration == null
				? key.Expiry
				: DateTime.SpecifyKind(contract.Expiration.Value.Date, DateTimeKind.Utc),
			Strike = contract.StrikePrice ?? key.Strike,
			OptionType = contract.OptionType switch
			{
				NasdaqCloudOptionTypes.Call => OptionTypes.Call,
				NasdaqCloudOptionTypes.Put => OptionTypes.Put,
				_ => key.OptionType,
			},
			Multiplier = 100m,
			UnderlyingSecurityId = new()
			{
				SecurityCode = contract.Symbol.IsEmpty(key.Root),
				BoardCode = "NCDS",
				Native = contract.Symbol.IsEmpty(key.Root),
			},
		};
	}

	public static bool Matches(this NasdaqCloudEquity equity, string value)
		=> value.IsEmpty() || equity.Symbol.EqualsIgnoreCase(value) ||
			equity.SecurityName.ContainsIgnoreCase(value);

	public static bool Matches(this NasdaqCloudIndex index, string value)
		=> value.IsEmpty() || index.Instrument.EqualsIgnoreCase(value) ||
			index.InstrumentName.ContainsIgnoreCase(value);

	public static bool Matches(this NasdaqCloudEtp etp, string value)
		=> value.IsEmpty() || etp.Symbol.EqualsIgnoreCase(value) ||
			etp.Name.ContainsIgnoreCase(value);

	public static SecurityId NormalizeNasdaqCloud(this SecurityId securityId,
		string code, string boardCode, string native)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(code);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(boardCode);
		securityId.Native ??= native;
		return securityId;
	}

	public static bool? ToUpTick(this int? value)
		=> value switch
		{
			> 0 => true,
			< 0 => false,
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
