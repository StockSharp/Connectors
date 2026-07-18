namespace StockSharp.Orats;

static class Extensions
{
	public const string StockBoard = "ORATS";
	public const string OptionBoard = "ORATSOPT";

	public static readonly TimeSpan[] TimeFrames = [TimeSpan.FromDays(1)];

	public static string ToBoard(this OratsMarkets market)
		=> market switch
		{
			OratsMarkets.Stocks => StockBoard,
			OratsMarkets.Options => OptionBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static OratsMarkets ToOratsMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(OptionBoard)
			? OratsMarkets.Options : OratsMarkets.Stocks;

	public static OratsSecurityKey GetOratsKey(this SecurityId securityId)
	{
		var native = securityId.Native as string;
		if (OratsSecurityKey.TryParse(native, out var key))
			return key;
		var code = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (OratsSecurityKey.TryParseOcc(code, out key))
			return key;
		var market = securityId.BoardCode.ToOratsMarket();
		if (market == OratsMarkets.Options)
		{
			throw new InvalidOperationException(
				"ORATS option identifiers must come from lookup or use OCC symbology.");
		}
		return new(OratsMarkets.Stocks, Normalize(code), default, default, null);
	}

	public static SecurityId NormalizeOrats(this SecurityId securityId, OratsSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.ToSecurityCode());
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this OratsTicker ticker,
		long originalTransactionId)
	{
		if (ticker?.Ticker.IsEmpty() != false)
			return null;
		var root = Normalize(ticker.Ticker);
		var key = new OratsSecurityKey(OratsMarkets.Stocks, root, default, default, null);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = root,
				BoardCode = StockBoard,
				Native = key.ToNative(),
			},
			Name = root,
			ShortName = root,
			SecurityType = SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
		};
	}

	public static SecurityMessage ToSecurityMessage(this OratsSecurityKey key,
		long originalTransactionId)
	{
		if (key.Market != OratsMarkets.Options || key.OptionType == null ||
			key.Root.IsEmpty() || key.Expiration == default || key.Strike < 0)
		{
			return null;
		}
		var code = key.ToSecurityCode();
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = code,
				BoardCode = OptionBoard,
				Native = key.ToNative(),
			},
			Name = code,
			ShortName = code,
			SecurityType = SecurityTypes.Option,
			Currency = CurrencyTypes.USD,
			ExpiryDate = key.Expiration,
			Strike = key.Strike,
			OptionType = key.OptionType,
			Multiplier = 100m,
			UnderlyingSecurityId = new()
			{
				SecurityCode = key.Root,
				BoardCode = StockBoard,
				Native = new OratsSecurityKey(OratsMarkets.Stocks, key.Root,
					default, default, null).ToNative(),
			},
		};
	}

	public static bool TryGetKey(this OratsSnapshot value, out OratsSecurityKey key)
	{
		key = default;
		if (value?.OptionSymbol.IsEmpty() == false &&
			OratsSecurityKey.TryParseOcc(value.OptionSymbol, out key))
		{
			return true;
		}
		if (value?.Ticker.IsEmpty() != false || value.Strike == null ||
			!TryParseDate(value.ExpirationDate, out var expiration) ||
			!TryParseOptionType(value.OptionType, out var optionType))
		{
			return false;
		}
		key = new(OratsMarkets.Options, Normalize(value.Ticker), expiration,
			value.Strike.Value, optionType);
		return true;
	}

	public static OratsSecurityKey GetOptionKey(this OratsStrike value,
		OptionTypes optionType)
	{
		if (value?.Ticker.IsEmpty() != false || value.Strike == null ||
			!TryParseDate(value.ExpirationDate, out var expiration))
		{
			return default;
		}
		return new(OratsMarkets.Options, Normalize(value.Ticker), expiration,
			value.Strike.Value, optionType);
	}

	public static bool Matches(this OratsTicker ticker, string value)
		=> value.IsEmpty() || ticker?.Ticker.ContainsIgnoreCase(value) == true;

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || !DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal |
			DateTimeStyles.AdjustToUniversal, out result))
		{
			return false;
		}
		result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
		return true;
	}

	public static bool TryParseDate(string value, out DateTime result)
	{
		result = default;
		if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var parsed))
		{
			return false;
		}
		result = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
		return true;
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

	public static DateTime FromMarketTime(this DateTime value, TimeZoneInfo marketTimeZone)
	{
		value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
		if (marketTimeZone.IsInvalidTime(value))
			throw new InvalidOperationException($"Invalid ORATS market time '{value:O}'.");
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
			$"ORATS market time zone '{id}' was not found or is invalid.");
	}

	public static DateTime EstimateFrom(DateTime to, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 10000) : 500;
		try
		{
			var result = to - TimeSpan.FromDays(checked(bars * 2));
			var first = new DateTime(2007, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			return result < first ? first : result;
		}
		catch (ArgumentOutOfRangeException)
		{
			return new DateTime(2007, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		}
	}

	public static bool TryGetOhlc(this OratsDaily value,
		OratsPriceAdjustments adjustment, out decimal open, out decimal high,
		out decimal low, out decimal close, out decimal volume)
	{
		var hasAdjusted = value.Open != null && value.High != null &&
			value.Low != null && value.Close != null;
		var hasUnadjusted = value.UnadjustedOpen != null &&
			value.UnadjustedHigh != null && value.UnadjustedLow != null &&
			value.UnadjustedClose != null;
		var isAdjusted = adjustment == OratsPriceAdjustments.Adjusted
			? hasAdjusted || !hasUnadjusted : !hasUnadjusted;
		var o = isAdjusted ? value.Open : value.UnadjustedOpen;
		var h = isAdjusted ? value.High : value.UnadjustedHigh;
		var l = isAdjusted ? value.Low : value.UnadjustedLow;
		var c = isAdjusted ? value.Close : value.UnadjustedClose;
		var v = isAdjusted ? value.Volume : value.UnadjustedVolume;
		open = o.GetValueOrDefault();
		high = h.GetValueOrDefault();
		low = l.GetValueOrDefault();
		close = c.GetValueOrDefault();
		volume = Math.Max(0, v.GetValueOrDefault());
		return o != null && h != null && l != null && c != null;
	}

	private static bool TryParseOptionType(string value, out OptionTypes optionType)
	{
		if (value.EqualsIgnoreCase("C") || value.EqualsIgnoreCase("CALL"))
		{
			optionType = OptionTypes.Call;
			return true;
		}
		if (value.EqualsIgnoreCase("P") || value.EqualsIgnoreCase("PUT"))
		{
			optionType = OptionTypes.Put;
			return true;
		}
		optionType = default;
		return false;
	}

	private static string Normalize(string value)
		=> value?.Trim().ToUpperInvariant();
}

readonly record struct OratsSecurityKey(OratsMarkets Market, string Root,
	DateTime Expiration, decimal Strike, OptionTypes? OptionType)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture),
			Escape(Root), Market == OratsMarkets.Options
				? Expiration.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : string.Empty,
			Market == OratsMarkets.Options
				? Strike.ToString(CultureInfo.InvariantCulture) : string.Empty,
			OptionType == OptionTypes.Call ? "C" :
			OptionType == OptionTypes.Put ? "P" : string.Empty);

	public string ToSecurityCode()
		=> Market == OratsMarkets.Options ? ToApiSymbol() : Root;

	public string ToApiSymbol()
	{
		if (Market != OratsMarkets.Options || OptionType == null)
			throw new InvalidOperationException("An ORATS option contract is required.");
		var scaledStrike = checked((long)decimal.Round(Strike * 1000m, 0,
			MidpointRounding.AwayFromZero));
		if (scaledStrike is < 0 or > 99999999)
			throw new InvalidOperationException($"ORATS option strike '{Strike}' is out of range.");
		return Root + Expiration.ToString("yyMMdd", CultureInfo.InvariantCulture) +
			(OptionType == OptionTypes.Call ? "C" : "P") +
			scaledStrike.ToString("D8", CultureInfo.InvariantCulture);
	}

	public static bool TryParseOcc(string value, out OratsSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		value = value.Trim();
		if (value.Length <= 15)
			return false;
		var suffix = value[^15..];
		var root = value[..^15].Trim().ToUpperInvariant();
		if (root.IsEmpty() || !DateTime.TryParseExact(suffix[..6], "yyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration) ||
			!long.TryParse(suffix[7..], NumberStyles.None, CultureInfo.InvariantCulture,
				out var scaledStrike))
		{
			return false;
		}
		var optionType = suffix[6] is 'C' or 'c' ? OptionTypes.Call :
			suffix[6] is 'P' or 'p' ? OptionTypes.Put : (OptionTypes?)null;
		if (optionType == null)
			return false;
		key = new(OratsMarkets.Options, root,
			DateTime.SpecifyKind(expiration.Date, DateTimeKind.Utc),
			scaledStrike / 1000m, optionType);
		return true;
	}

	public static bool TryParse(string value, out OratsSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 5 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(OratsMarkets), marketValue))
		{
			return false;
		}
		var market = (OratsMarkets)marketValue;
		var root = Unescape(parts[1])?.Trim().ToUpperInvariant();
		if (root.IsEmpty())
			return false;
		if (market == OratsMarkets.Stocks)
		{
			key = new(market, root, default, default, null);
			return true;
		}
		if (!DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var expiration) ||
			!decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture,
				out var strike))
		{
			return false;
		}
		var optionType = parts[4] == "C" ? OptionTypes.Call :
			parts[4] == "P" ? OptionTypes.Put : (OptionTypes?)null;
		if (optionType == null)
			return false;
		key = new(market, root,
			DateTime.SpecifyKind(expiration.Date, DateTimeKind.Utc), strike, optionType);
		return true;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}
