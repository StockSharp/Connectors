namespace StockSharp.FubonNeo;

static class FubonNeoExtensions
{
	private const string _stockPrefix = "S";
	private const string _futuresPrefix = "F";
	private static readonly TimeZoneInfo _taipeiTimeZone = CreateTaipeiTimeZone();

	public static string ToNativeKey(this FubonNeoSecurityInfo security)
		=> security.Kind == FubonNeoAssetKinds.Stock
			? string.Join('|', _stockPrefix, security.TickerType, security.Exchange, security.Symbol)
			: string.Join('|', _futuresPrefix, security.TickerType, security.Session, security.Symbol);

	public static FubonNeoSecurityInfo ParseFubonSecurity(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			var parts = native.Split('|');
			if (parts.Length != 4 || parts[3].IsEmpty())
				throw new FormatException($"Invalid Fubon Neo instrument key '{native}'.");
			return parts[0] switch
			{
				_stockPrefix => new()
				{
					Kind = FubonNeoAssetKinds.Stock,
					TickerType = parts[1],
					Exchange = parts[2],
					Symbol = parts[3],
				},
				_futuresPrefix => new()
				{
					Kind = FubonNeoAssetKinds.FuturesOptions,
					TickerType = parts[1],
					Exchange = "TAIFEX",
					Session = parts[2],
					Symbol = parts[3],
				},
				_ => throw new FormatException($"Invalid Fubon Neo market prefix in '{native}'."),
			};
		}

		var kind = securityType is SecurityTypes.Future or SecurityTypes.Option ||
			securityId.BoardCode?.StartsWith("TAIFEX", StringComparison.OrdinalIgnoreCase) == true
			? FubonNeoAssetKinds.FuturesOptions
			: FubonNeoAssetKinds.Stock;
		return new()
		{
			Kind = kind,
			TickerType = securityType switch
			{
				SecurityTypes.Future => "FUTURE",
				SecurityTypes.Option => "OPTION",
				SecurityTypes.Index => "INDEX",
				SecurityTypes.Warrant => "WARRANT",
				_ => "EQUITY",
			},
			Exchange = kind == FubonNeoAssetKinds.Stock
				? securityId.BoardCode.EqualsIgnoreCase("TPEX") ? "TPEx" : "TWSE"
				: "TAIFEX",
			Session = securityId.BoardCode.EqualsIgnoreCase("TAIFEX-AH") ? "AFTERHOURS" : "REGULAR",
			Symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)),
		};
	}

	public static SecurityId ToSecurityId(this FubonNeoSecurityInfo security)
		=> new()
		{
			SecurityCode = security.Symbol,
			BoardCode = security.Kind == FubonNeoAssetKinds.Stock
				? security.Exchange.EqualsIgnoreCase("TPEX") || security.Exchange.EqualsIgnoreCase("TPEx") ? "TPEX" : "TWSE"
				: security.IsAfterHours ? "TAIFEX-AH" : "TAIFEX",
			Native = security.ToNativeKey(),
		};

	public static SecurityTypes ToSecurityType(this FubonNeoSecurityInfo security)
		=> security.TickerType?.ToUpperInvariant() switch
		{
			"INDEX" => SecurityTypes.Index,
			"WARRANT" => SecurityTypes.Warrant,
			"FUTURE" or "FUTURE_AH" => SecurityTypes.Future,
			"OPTION" or "OPTION_AH" => SecurityTypes.Option,
			_ => SecurityTypes.Stock,
		};

	public static DateTime? GetExpiry(this FubonNeoSecurityInfo security)
		=> ParseDate(security.SettlementDate.IsEmpty(security.EndDate));

	public static DateTime? ParseFubonMarketTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			return DateTime.SpecifyKind(result, DateTimeKind.Utc);
		return null;
	}

	public static DateTime? ToFubonMarketTime(this long? microseconds)
	{
		if (microseconds is not > 0)
			return null;
		try
		{
			return DateTime.UnixEpoch.AddTicks(checked(microseconds.Value * 10));
		}
		catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
		{
			return null;
		}
	}

	public static DateTime? ParseFubonTradeTime(string date, string time)
	{
		if (date.IsEmpty())
			return null;
		var value = $"{date.Trim()} {time?.Trim()}";
		var formats = new[]
		{
			"yyyy/MM/dd H:mm:ss.FFFFFFF", "yyyy/MM/dd HH:mm:ss.FFFFFFF",
			"yyyyMMdd H:mm:ss.FFFFFFF", "yyyyMMdd HH:mm:ss.FFFFFFF",
			"yyyy-MM-dd H:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
		};
		if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _taipeiTimeZone);
	}

	public static string ToSubscriptionChannel(this FubonNeoSecurityInfo security, DataType dataType)
	{
		if (dataType == DataType.Ticks)
		{
			if (security.ToSecurityType() == SecurityTypes.Index)
				throw new NotSupportedException("Fubon index streams do not publish individual trades.");
			return "Trades";
		}
		if (dataType == DataType.MarketDepth)
		{
			if (security.ToSecurityType() == SecurityTypes.Index)
				throw new NotSupportedException("Fubon index streams do not publish order books.");
			return "Books";
		}
		if (dataType == DataType.Level1)
			return security.ToSecurityType() == SecurityTypes.Index ? "Indices" : "Aggregates";
		if (dataType.IsCandles)
			return "Candles";
		throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported Fubon market-data type.");
	}

	public static string ToFubonTimeFrame(this TimeSpan timeFrame, FubonNeoAssetKinds kind)
	{
		if (kind == FubonNeoAssetKinds.Stock)
		{
			if (timeFrame == TimeSpan.FromDays(1))
				return "Day";
			if (timeFrame == TimeSpan.FromDays(7))
				return "Week";
			if (timeFrame == TimeSpan.FromDays(30))
				return "Month";
			if (timeFrame.TotalMinutes is 1 or 3 or 5 or 10 or 15 or 30 or 60)
				return NumberTimeFrame(timeFrame);
		}
		else if (timeFrame.TotalMinutes is 1 or 5 or 10 or 15 or 30 or 60)
			return NumberTimeFrame(timeFrame);
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			$"Unsupported Fubon {(kind == FubonNeoAssetKinds.Stock ? "stock" : "futures/options")} candle interval.");
	}

	private static string NumberTimeFrame(TimeSpan timeFrame)
		=> timeFrame.TotalMinutes switch
		{
			1 => "OneMin",
			3 => "ThreeMin",
			5 => "FiveMin",
			10 => "TenMin",
			15 => "FifteenMin",
			30 => "ThirtyMin",
			60 => "SixtyMin",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame)),
		};

	private static DateTime? ParseDate(string value)
		=> DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd" },
			CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
			? DateTime.SpecifyKind(date, DateTimeKind.Utc)
			: null;

	private static TimeZoneInfo CreateTaipeiTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
		}
		catch (TimeZoneNotFoundException)
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
			}
			catch (TimeZoneNotFoundException)
			{
				return TimeZoneInfo.CreateCustomTimeZone("Taipei", TimeSpan.FromHours(8), "Taipei", "Taipei");
			}
		}
	}
}
