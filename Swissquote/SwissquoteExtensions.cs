namespace StockSharp.Swissquote;

internal static class SwissquoteExtensions
{
	public static string ToNative(this SwissquoteInstrumentIdentificationTypes type)
		=> type switch
		{
			SwissquoteInstrumentIdentificationTypes.Isin => "isin",
			SwissquoteInstrumentIdentificationTypes.Sedol => "sedol",
			SwissquoteInstrumentIdentificationTypes.Cusip => "cusip",
			SwissquoteInstrumentIdentificationTypes.Ric => "ric",
			SwissquoteInstrumentIdentificationTypes.TickerSymbol => "tickerSymbol",
			SwissquoteInstrumentIdentificationTypes.Bloomberg => "bloomberg",
			SwissquoteInstrumentIdentificationTypes.OtherProprietaryIdentification =>
				"otherProprietaryIdentification",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static SwissquoteInstrumentIdentificationTypes ToIdentificationType(this string value)
	{
		var normalized = NormalizeEnum(value);
		return normalized switch
		{
			"ISIN" => SwissquoteInstrumentIdentificationTypes.Isin,
			"SEDOL" => SwissquoteInstrumentIdentificationTypes.Sedol,
			"CUSIP" => SwissquoteInstrumentIdentificationTypes.Cusip,
			"RIC" => SwissquoteInstrumentIdentificationTypes.Ric,
			"TICKERSYMBOL" => SwissquoteInstrumentIdentificationTypes.TickerSymbol,
			"BLOOMBERG" => SwissquoteInstrumentIdentificationTypes.Bloomberg,
			_ => SwissquoteInstrumentIdentificationTypes.OtherProprietaryIdentification,
		};
	}

	public static string ToNative(this SwissquoteQuantityTypes type)
		=> type == SwissquoteQuantityTypes.Nominal ? "nominal" : "unitsNumber";

	public static string ToNative(this SwissquoteOptionStyles style)
		=> style switch
		{
			SwissquoteOptionStyles.American => "amer",
			SwissquoteOptionStyles.European => "eur",
			SwissquoteOptionStyles.Bermudan => "berm",
			_ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
		};

	public static string ToNative(this SwissquoteOptionExpirationTypes type)
		=> type switch
		{
			SwissquoteOptionExpirationTypes.Daily => "daily",
			SwissquoteOptionExpirationTypes.Weekly => "weekly",
			SwissquoteOptionExpirationTypes.Monthly => "monthly",
			SwissquoteOptionExpirationTypes.EndOfMonth => "end_of_the_month",
			SwissquoteOptionExpirationTypes.Quarterly => "quarterly",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static string ToNativeSide(this Sides side, bool isOpenPosition,
		OrderPositionEffects? positionEffect)
	{
		if (positionEffect == OrderPositionEffects.CloseOnly)
			return side == Sides.Buy ? "buyToClose" : "sellToClose";
		if (isOpenPosition)
			return side == Sides.Buy ? "buyToOpen" : "sellToOpen";
		return side == Sides.Buy ? "buy" : "sell";
	}

	public static Sides ToSide(this string side)
		=> side?.StartsWith("buy", StringComparison.OrdinalIgnoreCase) == true ? Sides.Buy : Sides.Sell;

	public static string ToExecutionType(this OrderTypes orderType, decimal limitPrice,
		decimal? stopPrice)
		=> orderType switch
		{
			OrderTypes.Market => "market",
			OrderTypes.Limit => "limit",
			OrderTypes.Conditional when stopPrice is > 0 && limitPrice > 0 => "stopLimit",
			OrderTypes.Conditional when stopPrice is > 0 => "stop",
			_ => throw new NotSupportedException($"Swissquote does not support {orderType} orders."),
		};

	public static OrderTypes ToOrderType(this string executionType)
		=> executionType?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOPLIMIT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToNativeTimeInForce(this TimeInForce? timeInForce,
		DateTime? expiryDate, bool isDigitalAsset)
	{
		if (expiryDate != null)
			return "goodTillDate";
		return timeInForce switch
		{
			TimeInForce.CancelBalance => "immediateOrCancel",
			TimeInForce.MatchOrCancel => "fillOrKill",
			_ when isDigitalAsset => "goodTillCancel",
			_ => "day",
		};
	}

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IMMEDIATEORCANCEL" => TimeInForce.CancelBalance,
			"FILLORKILL" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"ACKNOWLEDGED" or "VALIDATIONDONE" or "CUSTOMERRELEASE" or "PENDINGCANCEL" =>
				OrderStates.Pending,
			"ACCEPTED" or "PLACED" or "PARTIALLYFILLED" => OrderStates.Active,
			"FILLED" or "EXECUTED" or "CANCELLED" or "PARTIALLYCANCELLED" or
				"MARKETCANCELLED" or "EXPIRED" or "PARTIALLYEXPIRED" or "MARKETEXPIRED" =>
				OrderStates.Done,
			"REJECTED" or "PARTIALLYREJECTED" or "MARKETREJECTED" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
			? result : null;

	public static string ToNativeDecimal(this decimal value)
		=> value.ToString("0.#####################", CultureInfo.InvariantCulture);

	public static DateTime? ToDateTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		return DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal, out var result)
			? result.UtcKind() : null;
	}

	public static DateTime? ToDate(this string value)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
			? result : null;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var result) ? result : null;

	public static SecurityTypes ToSecurityType(this SwissquoteFinancialInstrument instrument)
	{
		var assetClass = instrument?.AssetClass?.ToUpperInvariant() ?? string.Empty;
		var identification = instrument?.FinancialInstrumentIdentification?.Identification ?? string.Empty;
		if (identification.StartsWith("ISO4217:", StringComparison.OrdinalIgnoreCase) ||
			assetClass.Contains("CASH", StringComparison.Ordinal))
			return SecurityTypes.Currency;
		if (identification.StartsWith("Crypto:", StringComparison.OrdinalIgnoreCase) ||
			assetClass.Contains("CRYPTO", StringComparison.Ordinal) ||
			assetClass.Contains("DIGITAL", StringComparison.Ordinal))
			return SecurityTypes.CryptoCurrency;
		if (instrument?.OptionDetails != null || assetClass.Contains("OPTION", StringComparison.Ordinal))
			return SecurityTypes.Option;
		if (assetClass.Contains("FUTURE", StringComparison.Ordinal))
			return SecurityTypes.Future;
		if (assetClass.Contains("BOND", StringComparison.Ordinal) ||
			assetClass.Contains("FIXED", StringComparison.Ordinal))
			return SecurityTypes.Bond;
		if (assetClass.Contains("ETF", StringComparison.Ordinal))
			return SecurityTypes.Etf;
		if (assetClass.Contains("FUND", StringComparison.Ordinal))
			return SecurityTypes.Fund;
		if (assetClass.Contains("FOREX", StringComparison.Ordinal) ||
			assetClass.Contains("FX", StringComparison.Ordinal))
			return SecurityTypes.Currency;
		return SecurityTypes.Stock;
	}

	public static SecurityId ToSecurityId(this SwissquoteInstrumentIdentification instrument,
		string boardCode = null)
	{
		var identification = instrument?.Identification.ThrowIfEmpty(nameof(instrument.Identification));
		var code = NormalizeSecurityCode(identification);
		var id = new SecurityId
		{
			SecurityCode = code,
			BoardCode = boardCode.IsEmpty("SWISSQUOTE").ToUpperInvariant(),
			Native = identification,
		};
		if (instrument.Type.ToIdentificationType() == SwissquoteInstrumentIdentificationTypes.Isin)
			id.Isin = identification;
		return id;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CALL" => OptionTypes.Call,
			"PUT" => OptionTypes.Put,
			_ => null,
		};

	public static string GetError(this SwissquoteCompleteOrder order)
	{
		var reasons = (order?.OrderState?.OrderCancellationReasonList ?? [])
			.Concat(order?.ExtendedOrder?.AllocationList?
				.SelectMany(allocation => allocation?.AllocationCancellationReasonList ?? []) ?? [])
			.Where(reason => reason != null)
			.Select(reason => reason.Proprietary.IsEmpty(reason.Code))
			.Where(reason => !reason.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return reasons.Length == 0 ? null : string.Join("; ", reasons);
	}

	private static string NormalizeSecurityCode(string identification)
	{
		if (identification.StartsWith("ISO4217:", StringComparison.OrdinalIgnoreCase))
			return identification[8..].Trim();
		if (identification.StartsWith("Crypto:", StringComparison.OrdinalIgnoreCase))
			return identification[7..].Trim();
		return identification;
	}

	private static string NormalizeEnum(string value)
	{
		if (value.IsEmpty())
			return string.Empty;
		var marker = "value=";
		var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (index >= 0)
		{
			value = value[(index + marker.Length)..];
			var end = value.IndexOf(')');
			if (end >= 0)
				value = value[..end];
		}
		return value.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
	}
}
