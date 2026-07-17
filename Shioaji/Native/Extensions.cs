namespace StockSharp.Shioaji.Native;

static class ShioajiExtensions
{
	private static readonly TimeZoneInfo _taipeiTimeZone = CreateTaipeiTimeZone();

	public static string ToNativeKey(this ShioajiContract contract)
		=> string.Join('|', contract.SecurityType, contract.Exchange, contract.Code, contract.TargetCode);

	public static ShioajiContract ParseShioajiContract(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			var parts = native.Split('|');
			if (parts.Length == 4 && !parts[0].IsEmpty() && !parts[2].IsEmpty())
			{
				return new()
				{
					SecurityType = parts[0],
					Region = "TW",
					Exchange = parts[1],
					Code = parts[2],
					TargetCode = parts[3],
				};
			}
			throw new FormatException($"Invalid Shioaji instrument key '{native}'.");
		}

		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return new()
		{
			SecurityType = (securityType ?? securityId.BoardCode.ToSecurityTypeFromBoard()).ToShioajiSecurityType(),
			Region = "TW",
			Exchange = securityId.BoardCode.ToShioajiExchange(securityType),
			Code = code,
		};
	}

	public static SecurityId ToSecurityId(this ShioajiContract contract)
		=> new()
		{
			SecurityCode = contract.Code,
			BoardCode = contract.Exchange.ToBoardCode(),
			Native = contract.ToNativeKey(),
		};

	public static SecurityTypes ToSecurityType(this ShioajiContract contract)
		=> contract.SecurityType.ToSecurityType();

	public static SecurityTypes ToSecurityType(this string securityType)
		=> securityType?.ToUpperInvariant() switch
		{
			"IND" => SecurityTypes.Index,
			"FUT" => SecurityTypes.Future,
			"OPT" => SecurityTypes.Option,
			"WRT" => SecurityTypes.Warrant,
			_ => SecurityTypes.Stock,
		};

	public static string ToShioajiSecurityType(this SecurityTypes securityType)
		=> securityType switch
		{
			SecurityTypes.Stock => "STK",
			SecurityTypes.Index => "IND",
			SecurityTypes.Future => "FUT",
			SecurityTypes.Option => "OPT",
			SecurityTypes.Warrant => "WRT",
			_ => throw new ArgumentOutOfRangeException(nameof(securityType), securityType,
				"Shioaji supports stocks, indices, futures, options, and warrants."),
		};

	public static string ToBoardCode(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"TSE" => "TWSE",
			"OTC" => "TPEX",
			"OES" => "TPEX",
			"TAIFEX" => "TAIFEX",
			_ => exchange.IsEmpty("TWSE"),
		};

	public static string ToShioajiExchange(this string boardCode, SecurityTypes? securityType = null)
	{
		if (securityType is SecurityTypes.Future or SecurityTypes.Option)
			return "TAIFEX";

		return boardCode?.ToUpperInvariant() switch
		{
			"TWSE" or "TSE" => "TSE",
			"TPEX" or "OTC" or "OES" => "OTC",
			"TAIFEX" => "TAIFEX",
			_ => securityType is SecurityTypes.Future or SecurityTypes.Option ? "TAIFEX" : "TSE",
		};
	}

	private static SecurityTypes ToSecurityTypeFromBoard(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"TAIFEX" => SecurityTypes.Future,
			_ => SecurityTypes.Stock,
		};

	public static CurrencyTypes? ToCurrency(this string currency)
		=> currency?.ToUpperInvariant() switch
		{
			"TWD" or "NTD" => CurrencyTypes.TWD,
			"USD" => CurrencyTypes.USD,
			"JPY" => CurrencyTypes.JPY,
			"CNY" => CurrencyTypes.CNY,
			_ => null,
		};

	public static OptionTypes? ToOptionType(this ShioajiContractInfo info)
		=> info.OptionRight.IsEmpty(info.CallPut)?.ToUpperInvariant() switch
		{
			"C" or "CALL" => OptionTypes.Call,
			"P" or "PUT" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ToExpiry(this ShioajiContractInfo info)
		=> ParseDate(info.ExpiryDate.IsEmpty(info.LastTradingDate).IsEmpty(info.DeliveryDate));

	public static Sides ToSide(this string action)
		=> action.EqualsIgnoreCase("Buy") ? Sides.Buy : Sides.Sell;

	public static string ToShioajiAction(this Sides side)
		=> side == Sides.Buy ? "Buy" : "Sell";

	public static string ToShioajiTimeInForce(this TimeInForce timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => "ROD",
		};

	public static TimeInForce ToTimeInForce(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderTypes ToOrderType(this string priceType)
		=> priceType?.ToUpperInvariant() switch
		{
			"MKT" or "MKP" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"FILLED" or "CANCELLED" => OrderStates.Done,
			"FAILED" => OrderStates.Failed,
			"PENDINGSUBMIT" => OrderStates.Pending,
			_ => OrderStates.Active,
		};

	public static DateTime? ParseTaiwanTime(string date, string time)
	{
		if (date.IsEmpty())
			return null;

		var value = time.IsEmpty() ? date : $"{date} {time}";
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;

		local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		return TimeZoneInfo.ConvertTimeToUtc(local, _taipeiTimeZone);
	}

	public static DateTime? ParseTaiwanTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		if (DateTime.TryParseExact(value, ["yyyyMMdd", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
		{
			exact = DateTime.SpecifyKind(exact, DateTimeKind.Unspecified);
			return TimeZoneInfo.ConvertTimeToUtc(exact, _taipeiTimeZone);
		}

		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
		{
			local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
			return TimeZoneInfo.ConvertTimeToUtc(local, _taipeiTimeZone);
		}
		return null;
	}

	public static DateTime? ParseOffsetTime(this string value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var result)
			? DateTime.SpecifyKind(result, DateTimeKind.Utc)
			: null;

	public static DateTime? ParseUnixTime(this double? seconds)
	{
		if (seconds is not > 0)
			return null;
		try
		{
			return DateTime.UnixEpoch.AddTicks(checked((long)(seconds.Value * TimeSpan.TicksPerSecond)));
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

	public static DateTime NormalizeUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToTaiwanLocal(this DateTime value)
		=> TimeZoneInfo.ConvertTimeFromUtc(value.NormalizeUtc(), _taipeiTimeZone);

	public static decimal? ToDecimalValue(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static bool IsSameInstrument(this ShioajiContract contract, string code, string exchange)
		=> (contract.Code.EqualsIgnoreCase(code) || contract.TargetCode.EqualsIgnoreCase(code)) &&
			(exchange.IsEmpty() || contract.Exchange.EqualsIgnoreCase(exchange) ||
				contract.Exchange.ToBoardCode().EqualsIgnoreCase(exchange.ToBoardCode()));

	private static DateTime? ParseDate(string value)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
			? DateTime.SpecifyKind(result, DateTimeKind.Utc)
			: null;

	private static TimeZoneInfo CreateTaipeiTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
		}
		catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
		}
	}
}
