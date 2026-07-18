namespace StockSharp.ActivFinancial;

static class ActivFinancialExtensions
{
	public const string BoardCode = "ACTIV";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static ActivSecurityKey GetActivKey(this SecurityId securityId,
		ActivDataSources defaultDataSource, ActivSymbologies defaultSymbology,
		string fallbackTimeZone)
	{
		if (ActivSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;

		var symbol = (securityId.Native as string).IsEmpty(securityId.SecurityCode)?.Trim();
		symbol.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new((int)defaultDataSource, (int)defaultSymbology, symbol,
			fallbackTimeZone.IsEmpty("UTC"));
	}

	public static SecurityId NormalizeActiv(this SecurityId securityId,
		ActivSecurityKey key, string board = null)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(board.IsEmpty(BoardCode));
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this ActivGatewayRecord record,
		long originalTransactionId, string fallbackTimeZone)
	{
		var key = record.ToKey(fallbackTimeZone);
		var board = ToBoardCode(record.Mic);
		var securityId = key.ToSecurityId(board);
		securityId.Isin = record.Isin;
		securityId.Cusip = record.Cusip;
		securityId.Sedol = record.Sedol;

		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = record.Name.IsEmpty(record.Description).IsEmpty(record.Symbol),
			ShortName = record.Symbol,
			Class = record.EntityType?.ToString(CultureInfo.InvariantCulture),
			SecurityType = record.EntityType.ToSecurityType(),
			Currency = record.Currency.ToCurrency(),
			ExpiryDate = record.Expiration.ToUtcDate(),
			Strike = Positive(record.StrikePrice),
			OptionType = record.OptionType.ToOptionType(),
			PriceStep = Positive(record.MinimumTick),
			Multiplier = Positive(record.ContractSize),
			VolumeStep = Positive(record.LotSize),
		};
		if (!record.UnderlyingSymbol.IsEmpty())
		{
			message.UnderlyingSecurityId = new ActivSecurityKey(record.DataSourceId,
				record.SymbologyId, record.UnderlyingSymbol,
				key.TimeZoneId).ToSecurityId(board);
		}
		return message;
	}

	public static ActivSecurityKey ToKey(this ActivGatewayRecord record,
		string fallbackTimeZone)
		=> new(record.DataSourceId, record.SymbologyId, record.Symbol,
			record.TimeZone.IsEmpty(fallbackTimeZone).IsEmpty("UTC"));

	public static string BuildLookupQuery(SecurityLookupMessage message,
		out bool isExactNative)
	{
		isExactNative = ActivSecurityKey.TryParse(message.SecurityId.Native as string,
			out var native);
		var symbol = isExactNative ? native.Symbol :
			(message.SecurityId.Native as string).IsEmpty(message.SecurityId.SecurityCode)?.Trim();
		if (symbol.IsEmpty())
			symbol = "*";
		else
		{
			var star = symbol.IndexOf('*');
			if (symbol.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) ||
				character is '=' or '!') || star >= 0 && star != symbol.Length - 1)
			{
				throw new ArgumentException(
					"ACTIV lookup symbols must be simple values with an optional trailing wildcard.",
					nameof(message));
			}
			if (!isExactNative && !symbol.EndsWith('*'))
				symbol += "*";
		}

		var type = GetQueryType(message.GetSecurityTypes());
		return type.IsEmpty() ? $"symbol={symbol}" : $"type={type} and symbol={symbol}";
	}

	public static DateTime GetEventTime(this ActivGatewayRecord record,
		string fallbackTimeZone, DateTime referenceUtc)
	{
		var timeZone = record.TimeZone.IsEmpty(fallbackTimeZone).IsEmpty("UTC");
		return record.DateTime.ToUtc(timeZone, referenceUtc) ??
			record.LastUpdateDateTime.ToUtc(timeZone, referenceUtc) ??
			Combine(record.TradeDate, record.TradeTime).ToUtc(timeZone, referenceUtc) ??
			record.TradeTime.ToUtc(timeZone, referenceUtc) ??
			record.BidTime.ToUtc(timeZone, referenceUtc) ??
			record.AskTime.ToUtc(timeZone, referenceUtc) ??
			referenceUtc.ToUtc();
	}

	public static DateTime? GetTradeTime(this ActivGatewayRecord record,
		string fallbackTimeZone, DateTime referenceUtc)
	{
		var timeZone = record.TimeZone.IsEmpty(fallbackTimeZone).IsEmpty("UTC");
		return record.DateTime.ToUtc(timeZone, referenceUtc) ??
			Combine(record.TradeDate, record.TradeTime).ToUtc(timeZone, referenceUtc) ??
			record.TradeTime.ToUtc(timeZone, referenceUtc);
	}

	public static DateTime? ToUtc(this ActivGatewayTimestamp timestamp,
		string timeZoneId, DateTime referenceUtc)
	{
		if (timestamp == null)
			return null;
		var zone = ResolveTimeZone(timeZoneId);
		var referenceLocal = TimeZoneInfo.ConvertTimeFromUtc(referenceUtc.ToUtc(), zone);
		var year = timestamp.Year ?? referenceLocal.Year;
		var month = timestamp.Month ?? referenceLocal.Month;
		var day = timestamp.Day ?? referenceLocal.Day;
		var hour = timestamp.Hour ?? 0;
		var minute = timestamp.Minute ?? 0;
		var second = timestamp.Second ?? 0;
		var local = new DateTime(year, month, day, hour, minute, second,
			DateTimeKind.Unspecified).AddTicks(timestamp.FractionTicks);
		if (zone.IsInvalidTime(local))
			throw new InvalidOperationException($"ACTIV timestamp '{local:O}' is invalid in time zone '{zone.Id}'.");
		return TimeZoneInfo.ConvertTimeToUtc(local, zone);
	}

	public static DateTime? ToUtcDate(this ActivGatewayTimestamp timestamp)
		=> timestamp?.Year is int year && timestamp.Month is int month &&
			timestamp.Day is int day
				? DateTime.SpecifyKind(new DateTime(year, month, day), DateTimeKind.Utc)
				: null;

	public static TimeZoneInfo ResolveTimeZone(string id)
	{
		id = id.IsEmpty("UTC");
		foreach (var candidate in GetTimeZoneCandidates(id))
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(candidate);
			}
			catch (TimeZoneNotFoundException)
			{
			}
			catch (InvalidTimeZoneException)
			{
			}
		}
		throw new InvalidOperationException($"ACTIV time zone '{id}' is not available on this system.");
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static decimal? Positive(decimal? value) => value is > 0 ? value : null;
	public static decimal? NonNegative(decimal? value) => value is >= 0 ? value : null;
	public static string ToBoardCode(string value)
		=> value.IsEmpty() ? BoardCode :
			new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant()
				.IsEmpty(BoardCode);

	private static IEnumerable<string> GetTimeZoneCandidates(string id)
	{
		yield return id;
		if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows))
			yield return windows;
		if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana))
			yield return iana;
	}

	private static ActivGatewayTimestamp Combine(ActivGatewayTimestamp date,
		ActivGatewayTimestamp time)
	{
		if (date == null && time == null)
			return null;
		return new()
		{
			Year = date?.Year,
			Month = date?.Month,
			Day = date?.Day,
			Hour = time?.Hour,
			Minute = time?.Minute,
			Second = time?.Second,
			FractionTicks = time?.FractionTicks ?? 0,
		};
	}

	private static string GetQueryType(HashSet<SecurityTypes> types)
	{
		if (types.Count != 1)
			return null;
		return types.First() switch
		{
			SecurityTypes.Stock or SecurityTypes.Etf or SecurityTypes.Adr or
				SecurityTypes.Gdr => "listing",
			SecurityTypes.Option => "option",
			SecurityTypes.Future => "future",
			SecurityTypes.Index => "index",
			SecurityTypes.Currency => "forex",
			SecurityTypes.Bond => "bond",
			_ => null,
		};
	}

	private static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	private static OptionTypes? ToOptionType(this string value)
	{
		if (value.IsEmpty())
			return null;
		return value.EqualsIgnoreCase("C") || value.EqualsIgnoreCase("CALL") || value == "1"
			? OptionTypes.Call
			: value.EqualsIgnoreCase("P") || value.EqualsIgnoreCase("PUT") || value == "2"
				? OptionTypes.Put : null;
	}

	private static SecurityTypes? ToSecurityType(this int? value)
		=> value switch
		{
			1 or 89 => SecurityTypes.Stock,
			2 or 3 or 4 or 8 or 40 or 80 or 90 => SecurityTypes.Option,
			5 or 53 => SecurityTypes.Index,
			6 or 12 or 14 or 21 or 22 or 46 or 47 or 57 or 59 or 60 or 67 or 68 or 77 or 78 => SecurityTypes.Bond,
			7 or 35 => SecurityTypes.Future,
			9 or 15 or 37 or 56 => SecurityTypes.Currency,
			10 or 11 or 17 or 51 or 61 or 70 or 71 or 72 or 73 or 74 or 75 => SecurityTypes.Fund,
			18 or 48 or 52 or 62 or 87 => SecurityTypes.Etf,
			24 => SecurityTypes.Warrant,
			38 or 58 or 82 or 83 or 84 or 85 or 88 => SecurityTypes.Cfd,
			50 => SecurityTypes.Cfd,
			55 => SecurityTypes.Adr,
			81 => SecurityTypes.CryptoCurrency,
			_ => null,
		};
}

readonly record struct ActivSecurityKey(
	int DataSourceId,
	int SymbologyId,
	string Symbol,
	string TimeZoneId)
{
	public SecurityId ToSecurityId(string board = null)
		=> new()
		{
			SecurityCode = Symbol,
			BoardCode = board.IsEmpty(ActivFinancialExtensions.BoardCode),
			Native = ToNative(),
		};

	public string ToNative()
		=> string.Join('|',
			DataSourceId.ToString(CultureInfo.InvariantCulture),
			SymbologyId.ToString(CultureInfo.InvariantCulture),
			Uri.EscapeDataString(Symbol ?? string.Empty),
			Uri.EscapeDataString(TimeZoneId ?? string.Empty));

	public static bool TryParse(string value, out ActivSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 4 ||
			!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var dataSourceId) || dataSourceId is < 0 or > 65535 ||
			!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var symbologyId) || symbologyId is < 0 or > 65535)
		{
			return false;
		}
		var symbol = Uri.UnescapeDataString(parts[2]);
		if (symbol.IsEmpty())
			return false;
		key = new(dataSourceId, symbologyId, symbol,
			Uri.UnescapeDataString(parts[3]).IsEmpty("UTC"));
		return true;
	}
}
