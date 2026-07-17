namespace StockSharp.PhillipPoems;

static class PhillipPoemsExtensions
{
	public const string DefaultMarket = "SG";
	public const string DefaultExchange = "SGX";
	public const string DefaultCurrency = "SGD";

	public static decimal? ToDecimalValue(this string value)
	{
		if (value.IsEmptyOrWhiteSpace())
			return null;

		var normalized = new string(value.Where(c => char.IsDigit(c) || c is '-' or '+' or '.' or ',')
			.ToArray()).Replace(",", string.Empty);
		if (normalized.IsEmpty() || normalized is "-" or "+" or ".")
			return null;
		return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign,
			CultureInfo.InvariantCulture, out var result) ? result : null;
	}

	public static decimal? ToThousands(this string value)
		=> value.ToDecimalValue() is decimal number ? number * 1000m : null;

	public static long? ToLongValue(this string value)
		=> value.ToDecimalValue() is decimal number && number >= long.MinValue && number <= long.MaxValue
			? decimal.ToInt64(decimal.Truncate(number))
			: null;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static SecurityTypes? ToSecurityType(this PoemsCounter counter)
		=> counter?.Product?.ToUpperInvariant() switch
		{
			"ST" => SecurityTypes.Stock,
			"CFD" or "CFDDM" => SecurityTypes.Cfd,
			"FT" => SecurityTypes.Future,
			"FX" or "FXMN" => SecurityTypes.Currency,
			"UT" => SecurityTypes.Fund,
			_ => null,
		};

	public static SecurityId ToSecurityId(this PoemsCounter counter, string defaultExchange)
		=> new()
		{
			SecurityCode = counter?.Symbol.IsEmpty(counter?.Code)
				.IsEmpty(ParseCounterId(counter?.CounterId)?.Code),
			BoardCode = counter?.Exchange.IsEmpty(ParseCounterId(counter?.CounterId)?.Exchange)
				.IsEmpty(defaultExchange),
		};

	public static SecurityId ToSecurityId(this PoemsPrice price, string defaultExchange)
		=> new()
		{
			SecurityCode = price?.Symbol.IsEmpty(ParseCounterId(price?.CounterId)?.Code),
			BoardCode = price?.Exchange.IsEmpty(ParseCounterId(price?.CounterId)?.Exchange)
				.IsEmpty(defaultExchange),
		};

	public static SecurityId ToSecurityId(this PoemsOrder order, string defaultExchange)
		=> new()
		{
			SecurityCode = order?.Symbol.IsEmpty(ParseCounterId(order?.CounterId)?.Code),
			BoardCode = order?.Exchange.IsEmpty(ParseCounterId(order?.CounterId)?.Exchange)
				.IsEmpty(defaultExchange),
		};

	public static SecurityId ToSecurityId(this PoemsHolding holding, string defaultExchange)
		=> new()
		{
			SecurityCode = holding?.Symbol.IsEmpty(ParseCounterId(holding?.CounterId)?.Code),
			BoardCode = holding?.Exchange.IsEmpty(ParseCounterId(holding?.CounterId)?.Exchange)
				.IsEmpty(defaultExchange),
		};

	public static PoemsCounterIdentity ParseCounterId(string counterId)
	{
		if (counterId.IsEmpty())
			return null;
		var parts = counterId.Split('/');
		if (parts.Length < 4)
			return null;
		return new()
		{
			Product = parts[0],
			Market = parts[1],
			Exchange = parts[2],
			Code = parts.Skip(3).Join("/"),
		};
	}

	public static string ToNativeExchange(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NASDAQ" => "NASD",
			"HKEX" => "SEHK",
			_ => exchange,
		};

	public static Sides ToSide(this string action)
		=> action?.ToUpperInvariant() is "2" or "5" or "SELL" or "SHORT SELL" or "SHORTSELL"
			? Sides.Sell : Sides.Buy;

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OR" or "OP" or "AD" or "Q" or "PO" or "PE" => OrderStates.Active,
			"WD" or "EX" or "FL" or "FILLED" or "CN" or "CA" or "CANCELLED" or
				"CANCELED" or "EXPIRED" => OrderStates.Done,
			"RJ" or "RE" or "ER" or "FAILED" or "REJECTED" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"SLO" or "STOP LIMIT" or "STOP LIMIT ORDER" or "LIT" or
				"LIMIT-IF-TOUCHED" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static DateTime ToUtc(this string value, string exchange, DateTime currentTime)
	{
		if (value.IsEmptyOrWhiteSpace())
			return currentTime.Kind == DateTimeKind.Utc ? currentTime : currentTime.ToUniversalTime();

		var zone = GetTimeZone(exchange);
		DateTime parsed;
		if (TimeSpan.TryParse(value.Trim(), CultureInfo.InvariantCulture, out var time))
		{
			var localDate = TimeZoneInfo.ConvertTimeFromUtc(
				currentTime.Kind == DateTimeKind.Utc ? currentTime : currentTime.ToUniversalTime(), zone).Date;
			parsed = localDate + time;
		}
		else if (!DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out parsed))
		{
			return currentTime.Kind == DateTimeKind.Utc ? currentTime : currentTime.ToUniversalTime();
		}

		parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
		return TimeZoneInfo.ConvertTimeToUtc(parsed, zone);
	}

	public static DateTime ToExchangeLocal(this DateTime value, string exchange)
	{
		var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		return TimeZoneInfo.ConvertTimeFromUtc(utc, GetTimeZone(exchange));
	}

	private static TimeZoneInfo GetTimeZone(string exchange)
	{
		var id = exchange?.ToUpperInvariant() switch
		{
			"NYSE" or "AMEX" or "NASD" or "NASDAQ" => "Eastern Standard Time",
			"LSE" => "GMT Standard Time",
			"TSE" => "Tokyo Standard Time",
			"FWB" => "W. Europe Standard Time",
			"SET" => "SE Asia Standard Time",
			"SEHK" or "HKEX" or "SSE" or "SZSE" => "China Standard Time",
			_ => "Singapore Standard Time",
		};
		return TimeZoneInfo.FindSystemTimeZoneById(id);
	}

	public static string GetSecurityKey(SecurityId securityId, string defaultExchange)
		=> $"{securityId.SecurityCode}@{securityId.BoardCode.IsEmpty(defaultExchange).ToNativeExchange()}";
}

sealed class PoemsCounterIdentity
{
	public string Product { get; set; }
	public string Market { get; set; }
	public string Exchange { get; set; }
	public string Code { get; set; }
}
