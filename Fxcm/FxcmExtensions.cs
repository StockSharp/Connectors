namespace StockSharp.Fxcm;

internal static class FxcmExtensions
{
	public const string BoardCode = BoardCodes.Fxcm;

	public static PairSet<TimeSpan, string> TimeFrames { get; } = new()
	{
		{ TimeSpan.FromMinutes(1), "m1" },
		{ TimeSpan.FromMinutes(5), "m5" },
		{ TimeSpan.FromMinutes(15), "m15" },
		{ TimeSpan.FromMinutes(30), "m30" },
		{ TimeSpan.FromHours(1), "H1" },
		{ TimeSpan.FromHours(2), "H2" },
		{ TimeSpan.FromHours(3), "H3" },
		{ TimeSpan.FromHours(4), "H4" },
		{ TimeSpan.FromHours(6), "H6" },
		{ TimeSpan.FromHours(8), "H8" },
		{ TimeSpan.FromDays(1), "D1" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerWeek), "W1" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "M1" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static SecurityId ToSecurityId(this string symbol, long? offerId = null)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCode,
			Native = offerId,
		};

	public static SecurityTypes? ToSecurityType(this int value)
		=> value switch
		{
			1 => SecurityTypes.Currency,
			2 or 7 => SecurityTypes.Index,
			3 or 5 => SecurityTypes.Commodity,
			4 => SecurityTypes.Bond,
			6 => SecurityTypes.Stock,
			9 => SecurityTypes.CryptoCurrency,
			_ => null,
		};

	public static string ToNative(this TimeInForce? timeInForce, DateTimeOffset? tillDate)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue when tillDate == null => "GTC",
			TimeInForce.PutInQueue when tillDate.Value.Date == DateTime.UtcNow.Date => "DAY",
			TimeInForce.PutInQueue => "GTD",
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToTimeInForce(this string value, string expiration, out DateTime? tillDate)
	{
		tillDate = null;
		switch (value?.ToUpperInvariant())
		{
			case null:
			case "":
				return null;
			case "GTC":
				return TimeInForce.PutInQueue;
			case "DAY":
				tillDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
				return TimeInForce.PutInQueue;
			case "GTD":
				if (!expiration.IsEmpty() && DateTime.TryParseExact(expiration, "yyyyMMdd",
					CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
					tillDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);
				return TimeInForce.PutInQueue;
			case "FOK":
				return TimeInForce.MatchOrCancel;
			case "IOC":
				return TimeInForce.CancelBalance;
			default:
				return null;
		}
	}

	public static DateTime? ToDateTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		foreach (var format in new[] { "yyyyMMdd", "MMddyyyyHHmmss", "MMddyyyyHHmmssfff" })
		{
			if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
				return DateTime.SpecifyKind(result, DateTimeKind.Utc);
		}

		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
			? parsed.UtcDateTime
			: null;
	}

	public static OrderStates? ToOrderState(this int status)
		=> status switch
		{
			0 => null,
			1 or 2 or 6 or 7 => OrderStates.Active,
			3 or 5 or 8 or 9 or 10 => OrderStates.Done,
			_ => null,
		};
}
