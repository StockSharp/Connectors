namespace StockSharp.Poloniex;

static class Extensions
{
	public static string ToNative(this Sides value)
	{
		return value switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string value)
	{
		return (value?.ToLowerInvariant()) switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToPoloniex(this TimeInForce? value)
	{
		return value switch
		{
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			null or TimeInForce.PutInQueue => "GTC",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToPoloniexInterval(this TimeSpan value)
		=> value switch
		{
			{ TotalMinutes: 1 } => "MINUTE_1",
			{ TotalMinutes: 5 } => "MINUTE_5",
			{ TotalMinutes: 10 } => "MINUTE_10",
			{ TotalMinutes: 15 } => "MINUTE_15",
			{ TotalMinutes: 30 } => "MINUTE_30",
			{ TotalHours: 1 } => "HOUR_1",
			{ TotalHours: 2 } => "HOUR_2",
			{ TotalHours: 4 } => "HOUR_4",
			{ TotalHours: 6 } => "HOUR_6",
			{ TotalHours: 12 } => "HOUR_12",
			{ TotalDays: 1 } => "DAY_1",
			{ TotalDays: 3 } => "DAY_3",
			{ TotalDays: 7 } => "WEEK_1",
			{ TotalDays: 30 } => "MONTH_1",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Poloniex does not support this candle interval."),
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"NEW" or "PARTIALLY_FILLED" or "PENDING_CANCEL" => OrderStates.Active,
			"FILLED" or "PARTIALLY_CANCELED" or "CANCELED" => OrderStates.Done,
			"FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode?.Replace('/', '_').ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		//if (currency.Length > 3 && currency[3] != '/')
		//	currency = currency.Insert(3, "/");

		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Poloniex,
		};
	}

	public static long? ToClientTransactionId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result
			: null;
}
