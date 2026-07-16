namespace StockSharp.LsegRealTime;

internal static class LsegRealTimeExtensions
{
	public static DateTime ToLsegTime(this string date, long? milliseconds, DateTime fallback)
	{
		fallback = fallback.UtcKind();
		if (milliseconds is null or < 0 or >= 86_400_000)
			return fallback;

		var day = fallback.Date;
		if (!date.IsEmpty())
		{
			if (!DateTime.TryParse(date, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var parsed))
			{
				return fallback;
			}
			day = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
		}

		return day.AddMilliseconds(milliseconds.Value);
	}

	public static CurrencyTypes? ToLsegCurrency(this string currency)
		=> Enum.TryParse<CurrencyTypes>(currency, true, out var value) ? value : null;

	public static string ToLsegBoard(this LsegWireFields fields)
	{
		if (fields == null)
			return "LSEG";
		var exchange = fields.Exchange2.IsEmpty(fields.Exchange);
		return exchange.IsEmpty() ? "LSEG" : exchange.Trim().ToUpperInvariant();
	}

	public static SecurityTypes ToLsegSecurityType(this string ric, string recordType)
	{
		switch (recordType?.Trim().ToUpperInvariant())
		{
			case "BOND":
			case "BONDS":
			case "FIXEDINCOME":
				return SecurityTypes.Bond;
			case "FUT":
			case "FUTURE":
			case "FUTURES":
				return SecurityTypes.Future;
			case "IDX":
			case "INDEX":
				return SecurityTypes.Index;
			case "OPT":
			case "OPTION":
			case "OPTIONS":
				return SecurityTypes.Option;
			case "FX":
			case "FOREX":
			case "CURRENCY":
				return SecurityTypes.Currency;
		}

		if (ric.IsEmpty())
			return SecurityTypes.Stock;
		if (ric[0] == '.')
			return SecurityTypes.Index;
		if (ric.Contains('='))
			return SecurityTypes.Currency;
		if (ric[0] == '/')
			return SecurityTypes.Future;
		return SecurityTypes.Stock;
	}

	public static Sides? ToLsegSide(this string side)
		=> side?.Trim().ToUpperInvariant() switch
		{
			"BID" or "BUY" => Sides.Buy,
			"ASK" or "SELL" => Sides.Sell,
			_ => null,
		};

	public static QuoteChangeActions ToLsegAction(this string action)
		=> action?.Trim().ToUpperInvariant() switch
		{
			"ADD" => QuoteChangeActions.New,
			"DELETE" => QuoteChangeActions.Delete,
			_ => QuoteChangeActions.Update,
		};
}
