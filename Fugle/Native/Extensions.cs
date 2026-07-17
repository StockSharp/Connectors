namespace StockSharp.Fugle.Native;

static class Extensions
{
	private const string _stockPrefix = "S";
	private const string _futuresPrefix = "F";

	public static string ToNativeKey(this FugleSecurityInfo security)
		=> security.Kind == FugleAssetKinds.Stock
			? string.Join('|', _stockPrefix, security.TickerType, security.Exchange, security.Symbol)
			: string.Join('|', _futuresPrefix, security.TickerType, security.Session, security.Symbol);

	public static FugleSecurityInfo ParseFugleSecurity(this SecurityId securityId)
	{
		if (securityId.Native is not string native || native.IsEmpty())
			throw new InvalidOperationException("Fugle instrument metadata is missing. Select the security through Fugle lookup so SecurityId.Native contains the market, type, session, and symbol.");

		var parts = native.Split('|');
		if (parts.Length != 4 || parts[3].IsEmpty())
			throw new FormatException($"Invalid Fugle instrument key '{native}'.");

		return parts[0] switch
		{
			_stockPrefix => new()
			{
				Kind = FugleAssetKinds.Stock,
				TickerType = parts[1],
				Exchange = parts[2],
				Symbol = parts[3],
			},
			_futuresPrefix => new()
			{
				Kind = FugleAssetKinds.FuturesOptions,
				TickerType = parts[1],
				Exchange = "TAIFEX",
				Session = parts[2],
				Symbol = parts[3],
			},
			_ => throw new FormatException($"Invalid Fugle market prefix in '{native}'."),
		};
	}

	public static SecurityId ToSecurityId(this FugleSecurityInfo security)
		=> new()
		{
			SecurityCode = security.Symbol,
			BoardCode = security.Kind == FugleAssetKinds.Stock
				? security.Exchange.EqualsIgnoreCase("TPEX") ? "TPEX" : "TWSE"
				: security.IsAfterHours ? "TAIFEX-AH" : "TAIFEX",
			Native = security.ToNativeKey(),
		};

	public static SecurityTypes ToSecurityType(this FugleSecurityInfo security)
		=> security.TickerType?.ToUpperInvariant() switch
		{
			"INDEX" => SecurityTypes.Index,
			"WARRANT" => SecurityTypes.Warrant,
			"FUTURE" or "FUTURE_AH" => SecurityTypes.Future,
			"OPTION" or "OPTION_AH" => SecurityTypes.Option,
			_ => SecurityTypes.Stock,
		};

	public static DateTime? GetExpiry(this FugleSecurityInfo security)
		=> ParseDate(security.SettlementDate.IsEmpty(security.EndDate));

	public static DateTime? ParseFugleTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			return DateTime.SpecifyKind(result, DateTimeKind.Utc);

		return null;
	}

	public static DateTime? ToFugleTime(this long? microseconds)
	{
		if (microseconds is not > 0)
			return null;

		try
		{
			return DateTime.UnixEpoch.AddTicks(checked(microseconds.Value * 10));
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	public static string ToFugleTimeFrame(this TimeSpan timeFrame, FugleAssetKinds kind)
	{
		if (kind == FugleAssetKinds.Stock)
		{
			if (timeFrame == TimeSpan.FromDays(1))
				return "D";
			if (timeFrame == TimeSpan.FromDays(7))
				return "W";
			if (timeFrame == TimeSpan.FromDays(30))
				return "M";
			if (timeFrame.TotalMinutes is 1 or 3 or 5 or 10 or 15 or 30 or 60)
				return ((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture);
		}
		else if (timeFrame.TotalMinutes is 1 or 5 or 10 or 15 or 30 or 60)
			return ((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture);

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			$"Unsupported Fugle {(kind == FugleAssetKinds.Stock ? "stock" : "futures/options")} candle interval.");
	}

	public static string ToSubscriptionChannel(this FugleSecurityInfo security, DataType dataType)
	{
		if (dataType == DataType.Ticks)
		{
			if (security.ToSecurityType() == SecurityTypes.Index)
				throw new NotSupportedException("Fugle index streams do not publish individual trades.");
			return "trades";
		}

		if (dataType == DataType.MarketDepth)
		{
			if (security.ToSecurityType() == SecurityTypes.Index)
				throw new NotSupportedException("Fugle index streams do not publish order books.");
			return "books";
		}

		if (dataType == DataType.Level1)
			return security.ToSecurityType() == SecurityTypes.Index ? "indices" : "aggregates";

		if (dataType.IsCandles)
			return "candles";

		throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported Fugle market-data type.");
	}

	private static DateTime? ParseDate(string value)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
			? DateTime.SpecifyKind(date, DateTimeKind.Utc)
			: null;
}
