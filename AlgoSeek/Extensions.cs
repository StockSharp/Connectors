namespace StockSharp.AlgoSeek;

static class Extensions
{
	public const string StockBoard = "ALGOSEEK";
	public const string OptionBoard = "ALGOSEEKOPT";
	public const string FuturesBoard = "ALGOSEEKFUT";
	public const string FutureOptionsBoard = "ALGOSEEKFOP";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromDays(1),
	];

	public static string ToBoard(this AlgoSeekMarkets market)
		=> market switch
		{
			AlgoSeekMarkets.Stocks => StockBoard,
			AlgoSeekMarkets.Options => OptionBoard,
			AlgoSeekMarkets.Futures => FuturesBoard,
			AlgoSeekMarkets.FutureOptions => FutureOptionsBoard,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static AlgoSeekMarkets ToAlgoSeekMarket(this string boardCode)
	{
		if (boardCode.EqualsIgnoreCase(OptionBoard))
			return AlgoSeekMarkets.Options;
		if (boardCode.EqualsIgnoreCase(FuturesBoard))
			return AlgoSeekMarkets.Futures;
		if (boardCode.EqualsIgnoreCase(FutureOptionsBoard))
			return AlgoSeekMarkets.FutureOptions;
		return AlgoSeekMarkets.Stocks;
	}

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekEquityTickRow value)
		=> new(AlgoSeekMarkets.Stocks, value.Ticker, null, null, null, null);

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekEquityMinuteRow value)
		=> new(AlgoSeekMarkets.Stocks, value.Ticker, null, null, null, null);

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekEquityDailyRow value)
		=> new(AlgoSeekMarkets.Stocks, value.Ticker, null, null, null,
			value.SecurityId);

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekOptionTickRow value)
		=> new(AlgoSeekMarkets.Options, value.Ticker,
			DateTime.SpecifyKind(value.Expiration.Date, DateTimeKind.Utc),
			value.Strike, value.OptionType, null);

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekOptionMinuteRow value)
		=> new(AlgoSeekMarkets.Options, value.Ticker,
			DateTime.SpecifyKind(value.Expiration.Date, DateTimeKind.Utc),
			value.Strike, value.OptionType, null);

	public static AlgoSeekSecurityKey ToKey(this AlgoSeekFuturesTickRow value)
		=> new(value.OptionType == null ? AlgoSeekMarkets.Futures :
			AlgoSeekMarkets.FutureOptions, value.Ticker, null, value.Strike,
			value.OptionType, value.SecurityId);

	public static SecurityId ToSecurityId(this AlgoSeekSecurityKey key)
		=> new()
		{
			SecurityCode = key.GetSecurityCode(),
			BoardCode = key.Market.ToBoard(),
			Native = key.ToNative(),
		};

	public static SecurityId NormalizeAlgoSeek(this SecurityId securityId,
		AlgoSeekSecurityKey key)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(key.GetSecurityCode());
		securityId.BoardCode = securityId.BoardCode.IsEmpty(key.Market.ToBoard());
		securityId.Native = key.ToNative();
		return securityId;
	}

	public static AlgoSeekSecurityKey GetAlgoSeekKey(this SecurityId securityId)
	{
		if (AlgoSeekSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;

		var market = securityId.BoardCode.ToAlgoSeekMarket();
		var code = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		code.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (market is AlgoSeekMarkets.Options or AlgoSeekMarkets.FutureOptions)
		{
			throw new InvalidOperationException(
				"AlgoSeek option subscriptions require a security returned by lookup so expiration, strike, and side remain unambiguous.");
		}
		return new(market, code, null, null, null, null);
	}

	public static SecurityMessage ToSecurityMessage(this AlgoSeekSecurityKey key,
		long originalTransactionId)
	{
		var code = key.GetSecurityCode();
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = key.ToSecurityId(),
			Name = code,
			ShortName = code,
			SecurityType = key.Market switch
			{
				AlgoSeekMarkets.Stocks => SecurityTypes.Stock,
				AlgoSeekMarkets.Futures => SecurityTypes.Future,
				_ => SecurityTypes.Option,
			},
			Currency = key.Market is AlgoSeekMarkets.Stocks or AlgoSeekMarkets.Options
				? CurrencyTypes.USD : null,
			ExpiryDate = key.Expiration,
			Strike = key.Strike,
			OptionType = key.OptionType,
		};
		if (key.Market == AlgoSeekMarkets.Options)
		{
			message.UnderlyingSecurityId = new AlgoSeekSecurityKey(
				AlgoSeekMarkets.Stocks, key.Symbol, null, null, null, null).ToSecurityId();
		}
		return message;
	}

	public static DateTime CombineMarketTime(DateTime date, string time,
		TimeZoneInfo marketTimeZone)
	{
		var value = DateTime.SpecifyKind(date.Date + ParseTime(time),
			DateTimeKind.Unspecified);
		if (marketTimeZone.IsInvalidTime(value))
			throw new InvalidOperationException($"Invalid AlgoSeek market time '{value:O}'.");
		return TimeZoneInfo.ConvertTimeToUtc(value, marketTimeZone);
	}

	public static DateTime CombineUtcTime(DateTime date, string time)
		=> DateTime.SpecifyKind(date.Date + ParseTime(time), DateTimeKind.Utc);

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToMarketDate(this DateTime value, TimeZoneInfo marketTimeZone)
		=> TimeZoneInfo.ConvertTimeFromUtc(value.ToUtc(), marketTimeZone).Date;

	public static bool InRange(DateTime time, MarketDataMessage message)
		=> (message.From == null || time >= message.From.Value.ToUtc()) &&
			(message.To == null || time <= message.To.Value.ToUtc());

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
			$"AlgoSeek market time zone '{id}' was not found or is invalid.");
	}

	private static TimeSpan ParseTime(string value)
	{
		value.ThrowIfEmpty(nameof(value));
		var compact = value.Trim().Replace(":", string.Empty);
		var point = compact.IndexOf('.');
		var whole = point >= 0 ? compact[..point] : compact;
		var fraction = point >= 0 ? compact[(point + 1)..] : string.Empty;

		if (point < 0 && whole.Length > 6)
		{
			fraction = whole[6..];
			whole = whole[..6];
		}
		if (whole.Length == 4)
			whole += "00";
		if (whole.Length != 6 || !int.TryParse(whole[..2], out var hour) ||
			!int.TryParse(whole.Substring(2, 2), out var minute) ||
			!int.TryParse(whole.Substring(4, 2), out var second) ||
			hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59 ||
			fraction.Any(character => !char.IsDigit(character)))
		{
			throw new FormatException($"Invalid AlgoSeek time '{value}'.");
		}

		var ticksText = fraction.Length >= 7 ? fraction[..7] :
			fraction.PadRight(7, '0');
		var ticks = ticksText.Length == 0 ? 0 :
			long.Parse(ticksText, CultureInfo.InvariantCulture);
		return new TimeSpan(hour, minute, second) + TimeSpan.FromTicks(ticks);
	}
}

readonly record struct AlgoSeekSecurityKey(
	AlgoSeekMarkets Market,
	string Symbol,
	DateTime? Expiration,
	decimal? Strike,
	OptionTypes? OptionType,
	string ProviderId)
{
	public string GetSecurityCode()
	{
		if (Market is AlgoSeekMarkets.Options)
		{
			return $"{Symbol} {Expiration:yyyyMMdd} {(OptionType == OptionTypes.Call ? "C" : "P")} {Strike?.ToString(CultureInfo.InvariantCulture)}";
		}
		return Symbol;
	}

	public string ToNative()
		=> string.Join('|',
			((int)Market).ToString(CultureInfo.InvariantCulture),
			Uri.EscapeDataString(Symbol ?? string.Empty),
			Expiration?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
			Strike?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
			OptionType == OptionTypes.Call ? "C" :
			OptionType == OptionTypes.Put ? "P" : string.Empty,
			Uri.EscapeDataString(ProviderId ?? string.Empty));

	public bool Matches(AlgoSeekSecurityKey other)
	{
		if (Market != other.Market || !Symbol.EqualsIgnoreCase(other.Symbol))
			return false;
		if (!ProviderId.IsEmpty() && !other.ProviderId.IsEmpty())
			return ProviderId.EqualsIgnoreCase(other.ProviderId);
		return Expiration?.Date == other.Expiration?.Date && Strike == other.Strike &&
			OptionType == other.OptionType;
	}

	public static bool TryParse(string value, out AlgoSeekSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 6 || !int.TryParse(parts[0], NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var marketValue) ||
			!Enum.IsDefined(typeof(AlgoSeekMarkets), marketValue))
		{
			return false;
		}

		var market = (AlgoSeekMarkets)marketValue;
		var symbol = Uri.UnescapeDataString(parts[1]);
		if (symbol.IsEmpty())
			return false;
		DateTime? expiration = null;
		decimal? strike = null;
		OptionTypes? optionType = null;
		if (market == AlgoSeekMarkets.Options)
		{
			if (!DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var date) ||
				!decimal.TryParse(parts[3], NumberStyles.Float,
					CultureInfo.InvariantCulture, out var strikeValue) ||
				parts[4] is not ("C" or "P"))
			{
				return false;
			}
			expiration = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
			strike = strikeValue;
			optionType = parts[4] == "C" ? OptionTypes.Call : OptionTypes.Put;
		}
		else if (market == AlgoSeekMarkets.FutureOptions)
		{
			if (!decimal.TryParse(parts[3], NumberStyles.Float,
				CultureInfo.InvariantCulture, out var strikeValue) ||
				parts[4] is not ("C" or "P"))
			{
				return false;
			}
			strike = strikeValue;
			optionType = parts[4] == "C" ? OptionTypes.Call : OptionTypes.Put;
		}
		key = new(market, symbol, expiration, strike, optionType,
			Uri.UnescapeDataString(parts[5]));
		return true;
	}
}
