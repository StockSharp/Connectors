namespace StockSharp.ThetaData;

static class Extensions
{
	public const string StockBoard = "THETASTOCK";
	public const string OptionBoard = "THETAOPTION";
	public const string IndexBoard = "THETAINDEX";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMilliseconds(10),
		TimeSpan.FromMilliseconds(100),
		TimeSpan.FromMilliseconds(500),
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string ToNative(this ThetaDataStockVenues venue)
		=> venue switch
		{
			ThetaDataStockVenues.NasdaqBasic => "nqb",
			ThetaDataStockVenues.UtpCta => "utp_cta",
			_ => throw new ArgumentOutOfRangeException(nameof(venue), venue, null),
		};

	public static string ToNative(this ThetaDataMarkets market)
		=> market switch
		{
			ThetaDataMarkets.Stocks => "stock",
			ThetaDataMarkets.Options => "option",
			ThetaDataMarkets.Indices => "index",
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static string ToNative(this ThetaDataStreamTypes streamType)
		=> streamType switch
		{
			ThetaDataStreamTypes.Trade => "TRADE",
			ThetaDataStreamTypes.Quote => "QUOTE",
			_ => throw new ArgumentOutOfRangeException(nameof(streamType), streamType, null),
		};

	public static string ToBoard(this ThetaDataMarkets market)
		=> market switch
		{
			ThetaDataMarkets.Stocks => StockBoard,
			ThetaDataMarkets.Options => OptionBoard,
			ThetaDataMarkets.Indices => IndexBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static ThetaDataMarkets ToThetaMarket(this string boardCode)
		=> boardCode.EqualsIgnoreCase(OptionBoard) ? ThetaDataMarkets.Options :
			boardCode.EqualsIgnoreCase(IndexBoard) ? ThetaDataMarkets.Indices :
			ThetaDataMarkets.Stocks;

	public static string ToThetaInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMilliseconds(10) ? "10ms" :
			timeFrame == TimeSpan.FromMilliseconds(100) ? "100ms" :
			timeFrame == TimeSpan.FromMilliseconds(500) ? "500ms" :
			timeFrame == TimeSpan.FromSeconds(1) ? "1s" :
			timeFrame == TimeSpan.FromSeconds(5) ? "5s" :
			timeFrame == TimeSpan.FromSeconds(10) ? "10s" :
			timeFrame == TimeSpan.FromSeconds(15) ? "15s" :
			timeFrame == TimeSpan.FromSeconds(30) ? "30s" :
			timeFrame == TimeSpan.FromMinutes(1) ? "1m" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5m" :
			timeFrame == TimeSpan.FromMinutes(10) ? "10m" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15m" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30m" :
			timeFrame == TimeSpan.FromHours(1) ? "1h" :
			throw new NotSupportedException(
				$"ThetaData does not document {timeFrame} intraday candles.");

	public static ThetaSecurityKey GetThetaKey(this SecurityId securityId)
	{
		var native = securityId.Native as string;
		if (ThetaSecurityKey.TryParse(native, out var key))
			return key;
		var code = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (TryParseOccCode(code, out key))
			return key;
		var market = securityId.BoardCode.ToThetaMarket();
		if (market == ThetaDataMarkets.Options)
			throw new InvalidOperationException(
				"ThetaData option identifiers must come from security lookup or use OCC symbology.");
		return new(market, Normalize(code), default, default, null);
	}

	public static SecurityId NormalizeTheta(this SecurityId securityId, ThetaSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.ToSecurityCode());
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static bool TryGetOptionKey(string value, out ThetaSecurityKey key)
		=> TryParseOccCode(value, out key);

	public static SecurityMessage ToSecurityMessage(this ThetaSymbol symbol,
		ThetaDataMarkets market, long originalTransactionId)
	{
		if (symbol?.Symbol.IsEmpty() != false || market == ThetaDataMarkets.Options)
			return null;
		var root = Normalize(symbol.Symbol);
		var key = new ThetaSecurityKey(market, root, default, default, null);
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = root,
				BoardCode = market.ToBoard(),
				Native = key.ToNative(),
			},
			Name = root,
			ShortName = root,
			SecurityType = market == ThetaDataMarkets.Indices
				? SecurityTypes.Index : SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
		};
	}

	public static SecurityMessage ToSecurityMessage(this ThetaOptionContract contract,
		long originalTransactionId)
	{
		if (!contract.TryGetKey(out var key))
			return null;
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
				Native = new ThetaSecurityKey(ThetaDataMarkets.Stocks, key.Root,
					default, default, null).ToNative(),
			},
		};
	}

	public static bool TryGetKey(this ThetaOptionContract contract, out ThetaSecurityKey key)
	{
		key = default;
		if (contract?.Symbol.IsEmpty() != false || contract.Strike == null ||
			!TryParseDate(contract.Expiration, out var expiration) ||
			!TryParseOptionType(contract.Right, out var optionType))
		{
			return false;
		}
		key = new(ThetaDataMarkets.Options, Normalize(contract.Symbol), expiration,
			contract.Strike.Value, optionType);
		return true;
	}

	public static bool Matches(this ThetaContract contract, ThetaSecurityKey key)
	{
		if (contract == null)
			return false;
		var root = contract.Symbol.IsEmpty(contract.Root);
		if (!root.EqualsIgnoreCase(key.Root))
			return false;
		if (key.Market != ThetaDataMarkets.Options)
			return true;
		return TryParseDate(contract.Expiration, out var expiration) &&
			expiration.Date == key.Expiration.Date && contract.Strike == key.Strike &&
			TryParseOptionType(contract.Right, out var optionType) &&
			optionType == key.OptionType;
	}

	public static ThetaSecurityKey ToThetaKey(this ThetaStreamContract contract)
	{
		if (contract?.Root.IsEmpty() != false)
			throw new InvalidDataException("ThetaData stream contract has no root symbol.");
		var market = contract.SecurityType?.ToUpperInvariant() switch
		{
			"OPTION" => ThetaDataMarkets.Options,
			"INDEX" => ThetaDataMarkets.Indices,
			_ => ThetaDataMarkets.Stocks,
		};
		if (market != ThetaDataMarkets.Options)
			return new(market, Normalize(contract.Root), default, default, null);
		if (contract.Expiration == null || contract.Strike == null ||
			!TryParseDate(contract.Expiration.Value, out var expiration) ||
			!TryParseOptionType(contract.Right, out var optionType))
		{
			throw new InvalidDataException("ThetaData stream contains an invalid option contract.");
		}
		return new(market, Normalize(contract.Root), expiration,
			contract.Strike.Value / 1000m, optionType);
	}

	public static ThetaStreamContract ToStreamContract(this ThetaSecurityKey key)
	{
		var result = new ThetaStreamContract { Root = key.Root };
		if (key.Market == ThetaDataMarkets.Options)
		{
			result.Expiration = int.Parse(key.Expiration.ToString("yyyyMMdd",
				CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
			result.Strike = checked((long)decimal.Round(key.Strike * 1000m, 0,
				MidpointRounding.AwayFromZero));
			result.Right = key.OptionType == OptionTypes.Call ? "C" : "P";
		}
		return result;
	}

	public static ThetaStreamKey ToStreamKey(this ThetaSecurityKey key, DataType dataType)
	{
		var streamType = dataType == DataType.Ticks ||
			key.Market == ThetaDataMarkets.Indices
			? ThetaDataStreamTypes.Trade : ThetaDataStreamTypes.Quote;
		return new(key, streamType);
	}

	public static bool TryParseMarketTime(string value, TimeZoneInfo marketTimeZone,
		out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || marketTimeZone == null || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
		{
			return false;
		}
		return TryConvertMarketTime(local, marketTimeZone, out result);
	}

	public static bool TryParseMarketDate(string value, TimeZoneInfo marketTimeZone,
		out DateTime result)
	{
		result = default;
		if (value.IsEmpty() || !DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
		{
			return false;
		}
		return TryConvertMarketTime(local.Date, marketTimeZone, out result);
	}

	public static bool TryGetStreamTime(int? date, long? millisecondsOfDay,
		TimeZoneInfo marketTimeZone, out DateTime result)
	{
		result = default;
		if (date == null || millisecondsOfDay is < 0 or >= 86400000 ||
			!TryParseDate(date.Value, out var localDate))
		{
			return false;
		}
		return TryConvertMarketTime(localDate.AddMilliseconds(millisecondsOfDay.Value),
			marketTimeZone, out result);
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
		if (!TryConvertMarketTime(value, marketTimeZone, out var result))
			throw new InvalidOperationException($"Invalid ThetaData market time '{value:O}'.");
		return result;
	}

	public static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		var bars = count is > 0 ? Math.Min(count.Value, 250000) : 500;
		var factor = timeFrame < TimeSpan.FromDays(1) ? 3L : 2L;
		try
		{
			var result = to - TimeSpan.FromTicks(
				checked(timeFrame.Ticks * bars * factor));
			return result < new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc)
				? new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc) : result;
		}
		catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
		{
			return new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		}
	}

	public static TimeZoneInfo ResolveMarketTimeZone(string id)
	{
		id.ThrowIfEmpty(nameof(id));
		foreach (var candidate in new[] { id, id.EqualsIgnoreCase("America/New_York")
			? "Eastern Standard Time" : null })
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
			$"ThetaData market time zone '{id}' was not found or is invalid.");
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

	private static bool TryParseDate(int value, out DateTime result)
	{
		result = default;
		if (!DateTime.TryParseExact(value.ToString(CultureInfo.InvariantCulture), "yyyyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			return false;
		}
		result = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified);
		return true;
	}

	private static bool TryConvertMarketTime(DateTime local, TimeZoneInfo marketTimeZone,
		out DateTime result)
	{
		result = default;
		try
		{
			local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
			if (marketTimeZone.IsInvalidTime(local))
				return false;
			result = TimeZoneInfo.ConvertTimeToUtc(local, marketTimeZone);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
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

	private static bool TryParseOccCode(string value, out ThetaSecurityKey key)
	{
		key = default;
		if (value?.Length != 21)
			return false;
		var root = Normalize(value[..6].Trim());
		if (root.IsEmpty() || !DateTime.TryParseExact(value.Substring(6, 6), "yyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration) ||
			!TryParseOptionType(value.Substring(12, 1), out var optionType) ||
			!long.TryParse(value.Substring(13, 8), NumberStyles.None,
				CultureInfo.InvariantCulture, out var scaledStrike))
		{
			return false;
		}
		key = new(ThetaDataMarkets.Options, root,
			DateTime.SpecifyKind(expiration.Date, DateTimeKind.Utc),
			scaledStrike / 1000m, optionType);
		return true;
	}

	private static string Normalize(string value)
		=> value?.Trim().ToUpperInvariant();
}

readonly record struct ThetaSecurityKey(ThetaDataMarkets Market, string Root,
	DateTime Expiration, decimal Strike, OptionTypes? OptionType)
{
	public string ToNative()
		=> string.Join('|', ((int)Market).ToString(CultureInfo.InvariantCulture),
			Escape(Root), Market == ThetaDataMarkets.Options
				? Expiration.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : string.Empty,
			Market == ThetaDataMarkets.Options
				? Strike.ToString(CultureInfo.InvariantCulture) : string.Empty,
			OptionType == OptionTypes.Call ? "C" : OptionType == OptionTypes.Put ? "P" : string.Empty);

	public string ToSecurityCode()
	{
		if (Market != ThetaDataMarkets.Options)
			return Root;
		var scaledStrike = checked((long)decimal.Round(Strike * 1000m, 0,
			MidpointRounding.AwayFromZero));
		if (Root.Length <= 6 && scaledStrike is >= 0 and <= 99999999)
		{
			return Root.PadRight(6) + Expiration.ToString("yyMMdd",
				CultureInfo.InvariantCulture) + (OptionType == OptionTypes.Call ? "C" : "P") +
				scaledStrike.ToString("D8", CultureInfo.InvariantCulture);
		}
		return $"{Root}_{Expiration:yyyyMMdd}_{Strike.ToString(CultureInfo.InvariantCulture)}_" +
			(OptionType == OptionTypes.Call ? "C" : "P");
	}

	public static bool TryParse(string value, out ThetaSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 5 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(ThetaDataMarkets), marketValue))
		{
			return false;
		}
		var market = (ThetaDataMarkets)marketValue;
		var root = Unescape(parts[1]);
		if (root.IsEmpty())
			return false;
		if (market != ThetaDataMarkets.Options)
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
		key = new(market, root, DateTime.SpecifyKind(expiration.Date, DateTimeKind.Utc),
			strike, optionType);
		return true;
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Unescape(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}

readonly record struct ThetaStreamKey(ThetaSecurityKey Security, ThetaDataStreamTypes Type);
