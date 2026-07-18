namespace StockSharp.OptionMetrics;

static class Extensions
{
	public const string StockBoard = "IVYDB";
	public const string OptionBoard = "IVYDBOPT";

	public static readonly TimeSpan[] TimeFrames = [TimeSpan.FromDays(1)];

	public static string ToBoard(this IvyDbMarkets market)
		=> market switch
		{
			IvyDbMarkets.Stocks => StockBoard,
			IvyDbMarkets.Options => OptionBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static IvyDbMarkets ToIvyDbMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(OptionBoard)
			? IvyDbMarkets.Options : IvyDbMarkets.Stocks;

	public static IvyDbSecurityKey ToKey(this IvyDbSecurityRow value)
		=> new(IvyDbMarkets.Stocks, value.SecurityId, null,
			IvyDbSecurityMaster.GetSymbol(value), null, null, null);

	public static IvyDbSecurityKey ToKey(this IvyDbOptionPriceRow value)
		=> new(IvyDbMarkets.Options, value.SecurityId, value.OptionId, value.Symbol,
			value.Expiration, value.Strike, value.OptionType);

	public static SecurityMessage ToSecurityMessage(this IvyDbSecurityRow value,
		IvyDbSecurityMaster master, long originalTransactionId)
	{
		if (value == null)
			return null;
		var code = IvyDbSecurityMaster.GetSymbol(value);
		if (code.IsEmpty())
			return null;
		var name = master.GetName(value.SecurityId)?.GetDescription();
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = value.ToKey().ToSecurityId(),
			Name = name.IsEmpty(code),
			ShortName = code,
			Class = GetExchangeClass(value.ExchangeFlags),
			SecurityType = value.ToSecurityType(),
			Currency = CurrencyTypes.USD,
		};
	}

	public static SecurityMessage ToSecurityMessage(this IvyDbOptionPriceRow value,
		IvyDbSecurityMaster master, long originalTransactionId)
	{
		if (value?.Symbol.IsEmpty() != false)
			return null;
		var underlyingSymbol = master.GetSymbol(value.SecurityId);
		var underlyingKey = new IvyDbSecurityKey(IvyDbMarkets.Stocks,
			value.SecurityId, null, underlyingSymbol, null, null, null);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = value.ToKey().ToSecurityId(),
			Name = value.Symbol,
			ShortName = value.Symbol,
			SecurityType = SecurityTypes.Option,
			Currency = CurrencyTypes.USD,
			ExpiryDate = value.Expiration,
			Strike = value.Strike,
			OptionType = value.OptionType,
			Multiplier = value.ContractSize is > 0 ? value.ContractSize : null,
			UnderlyingSecurityId = underlyingKey.ToSecurityId(),
		};
	}

	public static SecurityId ToSecurityId(this IvyDbSecurityKey key)
		=> new()
		{
			SecurityCode = key.Symbol,
			BoardCode = key.Market.ToBoard(),
			Native = key.ToNative(),
		};

	public static SecurityId NormalizeIvyDb(this SecurityId securityId,
		IvyDbSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.Symbol);
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static IvyDbSecurityRequest GetIvyDbRequest(this SecurityId securityId,
		IvyDbSecurityMaster master)
	{
		if (IvyDbSecurityKey.TryParse(securityId.Native as string, out var key))
			return new(key.Market, key, key.Symbol);
		if (securityId.Native is long securityIdValue)
		{
			var security = master.Find(securityIdValue) ??
				throw new InvalidOperationException(
					$"IvyDB Security ID '{securityIdValue}' is absent from the current Security table.");
			key = security.ToKey();
			return new(key.Market, key, key.Symbol);
		}

		var market = securityId.BoardCode.ToIvyDbMarket();
		var code = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		if (market == IvyDbMarkets.Options)
		{
			code.ThrowIfEmpty(nameof(securityId.SecurityCode));
			return new(market, null, code);
		}

		var matches = master.FindAll(code);
		if (matches.Count == 0)
		{
			throw new InvalidOperationException(
				$"IvyDB security '{code}' is absent from the current Security and Security Name tables.");
		}
		if (matches.Count > 1)
		{
			throw new InvalidOperationException(
				$"IvyDB security alias '{code}' is ambiguous. Use a security returned by lookup.");
		}
		var row = matches[0];
		key = row.ToKey();
		return new(key.Market, key, key.Symbol);
	}

	public static SecurityTypes ToSecurityType(this IvyDbSecurityRow value)
	{
		if (value.IsIndex || value.IssueType == IvyDbIssueTypes.Index ||
			(value.ExchangeFlags & 32768) != 0)
		{
			return SecurityTypes.Index;
		}
		return value.IssueType switch
		{
			IvyDbIssueTypes.ExchangeTradedFund => SecurityTypes.Etf,
			IvyDbIssueTypes.Fund => SecurityTypes.Fund,
			_ => SecurityTypes.Stock,
		};
	}

	public static decimal? AdjustPrice(this decimal? value,
		IvyDbSecurityPriceRow row, IvyDbAdjustmentFactors latest,
		IvyDbPriceAdjustments adjustment)
	{
		if (value == null || adjustment == IvyDbPriceAdjustments.Raw)
			return value;
		var rowFactor = adjustment == IvyDbPriceAdjustments.TotalReturnAdjusted
			? row.TotalReturnAdjustmentFactor ?? row.AdjustmentFactor
			: row.AdjustmentFactor;
		var latestFactor = adjustment == IvyDbPriceAdjustments.TotalReturnAdjusted
			? latest.TotalReturn : latest.Split;
		if (rowFactor is not > 0 || latestFactor <= 0)
			return value;
		return value.Value * rowFactor.Value / latestFactor;
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToMarketDate(this DateTime value, TimeZoneInfo marketTimeZone)
		=> TimeZoneInfo.ConvertTimeFromUtc(value.ToUtc(), marketTimeZone).Date;

	public static DateTime FromMarketTime(this DateTime value,
		TimeZoneInfo marketTimeZone)
	{
		value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
		if (marketTimeZone.IsInvalidTime(value))
			throw new InvalidOperationException($"Invalid IvyDB market time '{value:O}'.");
		return TimeZoneInfo.ConvertTimeToUtc(value, marketTimeZone);
	}

	public static TimeZoneInfo ResolveMarketTimeZone(string id)
	{
		id.ThrowIfEmpty(nameof(id));
		foreach (var candidate in new[]
		{
			id,
			id.EqualsIgnoreCase("America/New_York") ? "Eastern Standard Time" : null,
		})
		{
			if (candidate.IsEmpty())
				continue;
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
		throw new InvalidOperationException(
			$"IvyDB market time zone '{id}' was not found or is invalid.");
	}

	public static bool TryGetOccExpiration(string symbol, out DateTime expiration)
	{
		expiration = default;
		var compact = symbol?.Replace(" ", string.Empty);
		if (compact.IsEmpty() || compact.Length <= 15)
			return false;
		var suffix = compact[^15..];
		if (!DateTime.TryParseExact(suffix[..6], "yyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var value) || suffix[6] is not ('C' or 'c' or 'P' or 'p'))
		{
			return false;
		}
		expiration = DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
		return true;
	}

	private static string GetExchangeClass(int flags)
	{
		var values = new List<string>();
		if ((flags & 1) != 0) values.Add("NYSE/ARCA");
		if ((flags & 2) != 0) values.Add("AMEX");
		if ((flags & 4) != 0) values.Add("NASDAQ NMS");
		if ((flags & 8) != 0) values.Add("NASDAQ SmallCap");
		if ((flags & 16) != 0) values.Add("OTC BB");
		if ((flags & 32) != 0) values.Add("Cboe BZX");
		if ((flags & 64) != 0) values.Add("IEX");
		if ((flags & 32768) != 0) values.Add("Index");
		return string.Join(", ", values);
	}
}

readonly record struct IvyDbSecurityKey(
	IvyDbMarkets Market,
	long SecurityId,
	long? OptionId,
	string Symbol,
	DateTime? Expiration,
	decimal? Strike,
	OptionTypes? OptionType)
{
	public string ToNative()
		=> string.Join('|',
			((int)Market).ToString(CultureInfo.InvariantCulture),
			SecurityId.ToString(CultureInfo.InvariantCulture),
			OptionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
			Escape(Symbol),
			Expiration?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
			Strike?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
			OptionType == OptionTypes.Call ? "C" :
			OptionType == OptionTypes.Put ? "P" : string.Empty);

	public static bool TryParse(string value, out IvyDbSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 7 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(IvyDbMarkets), marketValue) ||
			!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var securityId) || securityId <= 0)
		{
			return false;
		}

		var market = (IvyDbMarkets)marketValue;
		var symbol = Unescape(parts[3]);
		if (symbol.IsEmpty())
			return false;
		if (market == IvyDbMarkets.Stocks)
		{
			key = new(market, securityId, null, symbol, null, null, null);
			return true;
		}

		if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var optionId) || optionId <= 0 ||
			!DateTime.TryParseExact(parts[4], "yyyyMMdd", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var expiration) ||
			!decimal.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture,
				out var strike) || strike < 0)
		{
			return false;
		}
		var optionType = parts[6] == "C" ? OptionTypes.Call :
			parts[6] == "P" ? OptionTypes.Put : (OptionTypes?)null;
		if (optionType == null)
			return false;
		key = new(market, securityId, optionId, symbol,
			DateTime.SpecifyKind(expiration.Date, DateTimeKind.Utc), strike, optionType);
		return true;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}

readonly record struct IvyDbSecurityRequest(
	IvyDbMarkets Market,
	IvyDbSecurityKey? Key,
	string Symbol)
{
	public bool Matches(IvyDbOptionPriceRow value)
	{
		if (value == null)
			return false;
		if (Key is { } key)
		{
			return key.Market == IvyDbMarkets.Options &&
				value.SecurityId == key.SecurityId &&
				value.OptionId == key.OptionId;
		}
		return value.MatchesSymbol(Symbol);
	}
}
