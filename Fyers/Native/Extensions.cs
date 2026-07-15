namespace StockSharp.Fyers.Native;

static class Extensions
{
	public static string ToNativeKey(this string symbol, string token)
		=> token.IsEmpty() ? symbol.ThrowIfEmpty(nameof(symbol)) : $"{symbol.ThrowIfEmpty(nameof(symbol))}|{token}";

	public static (string symbol, string token) ParseNativeKey(this string nativeKey)
	{
		nativeKey.ThrowIfEmpty(nameof(nativeKey));
		var separator = nativeKey.LastIndexOf('|');
		return separator > 0 ? (nativeKey[..separator], nativeKey[(separator + 1)..]) : (nativeKey, null);
	}

	public static string ToFyersSymbol(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return native.ParseNativeKey().symbol;
		if (securityId.SecurityCode?.Contains(':') == true)
			return securityId.SecurityCode;

		throw new InvalidOperationException("FYERS symbol is missing. Select the security through the FYERS lookup so SecurityId.Native contains the API symbol and token.");
	}

	public static string ToFyersToken(this SecurityId securityId)
		=> securityId.Native is string native && !native.IsEmpty() ? native.ParseNativeKey().token : null;

	public static SecurityId ToSecurityId(this FyersInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = instrument.ToBoardCode(),
			Native = instrument.Symbol.ToNativeKey(instrument.Token),
		};

	public static SecurityId ToFyersSecurityId(this string symbol, string token = null)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = ToBoardCode(token, symbol),
			Native = symbol.IsEmpty() ? null : symbol.ToNativeKey(token),
		};

	private static string ToBoardCode(string token, string symbol)
	{
		if (token?.Length >= 4)
		{
			var boardCode = token[..4] switch
			{
				"1010" => "NSE",
				"1011" => "NFO",
				"1012" => "CDS",
				"1020" => "NCO",
				"1120" => "MCX",
				"1210" => "BSE",
				"1211" or "1212" => "BFO",
				_ => null,
			};

			if (boardCode != null)
				return boardCode;
		}

		var separator = symbol?.IndexOf(':') ?? -1;
		return separator > 0 ? symbol[..separator] : null;
	}

	public static string ToBoardCode(this FyersInstrument instrument)
		=> (instrument.Exchange, instrument.Segment) switch
		{
			(FyersExchanges.Nse, FyersSegments.Cash) => "NSE",
			(FyersExchanges.Nse, FyersSegments.EquityDerivatives) => "NFO",
			(FyersExchanges.Nse, FyersSegments.CurrencyDerivatives) => "CDS",
			(FyersExchanges.Nse, FyersSegments.CommodityDerivatives) => "NCO",
			(FyersExchanges.Bse, FyersSegments.Cash) => "BSE",
			(FyersExchanges.Bse, _) => "BFO",
			(FyersExchanges.Mcx, _) => "MCX",
			_ => throw new ArgumentOutOfRangeException(nameof(instrument), $"Unsupported FYERS exchange/segment {instrument.Exchange}/{instrument.Segment}."),
		};

	public static SecurityTypes ToSecurityType(this FyersInstrument instrument)
	{
		if (instrument.Symbol?.EndsWith("-INDEX", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Index;
		if (instrument.OptionType is "CE" or "PE")
			return SecurityTypes.Option;
		if (instrument.ExpiryDate != null && instrument.Strike is null or 0)
			return SecurityTypes.Future;
		if (instrument.Segment == FyersSegments.CurrencyDerivatives)
			return SecurityTypes.Currency;
		if (instrument.Exchange == FyersExchanges.Mcx || instrument.Segment == FyersSegments.CommodityDerivatives)
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.ToUpperInvariant() switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static string ToHsmTopic(this FyersInstrument instrument, bool isDepth)
	{
		var segment = instrument.Token?.Length >= 4 ? instrument.Token[..4] : null;
		var segmentName = segment switch
		{
			"1010" => "nse_cm",
			"1011" => "nse_fo",
			"1012" => "cde_fo",
			"1020" => "nse_com",
			"1120" => "mcx_fo",
			"1210" => "bse_cm",
			"1211" => "bse_fo",
			"1212" => "bcs_fo",
			_ => throw new InvalidOperationException($"FYERS token '{instrument.Token}' has an unsupported exchange segment."),
		};

		if (isDepth && instrument.Symbol?.EndsWith("-INDEX", StringComparison.OrdinalIgnoreCase) == true)
			throw new InvalidOperationException("FYERS does not provide market depth for index symbols.");

		var exchangeToken = instrument.Token.Length > 10 ? instrument.Token[10..] : instrument.Token[4..];
		if (!isDepth && instrument.Symbol?.EndsWith("-INDEX", StringComparison.OrdinalIgnoreCase) == true)
			exchangeToken = instrument.Name.IsEmpty()
				? instrument.Symbol.Split(':').Last().Replace("-INDEX", string.Empty, StringComparison.OrdinalIgnoreCase)
				: instrument.Name;

		return $"{(isDepth ? "dp" : instrument.Symbol.EndsWith("-INDEX", StringComparison.OrdinalIgnoreCase) ? "if" : "sf")}|{segmentName}|{exchangeToken}";
	}

	public static FyersSides ToNative(this Sides side) => side == Sides.Buy ? FyersSides.Buy : FyersSides.Sell;
	public static Sides ToSide(this FyersSides side) => side == FyersSides.Buy ? Sides.Buy : Sides.Sell;

	public static FyersValidityTypes ToNative(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? FyersValidityTypes.ImmediateOrCancel : FyersValidityTypes.Day;

	public static TimeInForce ToTimeInForce(this FyersValidityTypes validity)
		=> validity == FyersValidityTypes.ImmediateOrCancel ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static FyersApiOrderTypes ToNative(this OrderTypes orderType, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market when triggerPrice is > 0 => FyersApiOrderTypes.Stop,
			OrderTypes.Market => FyersApiOrderTypes.Market,
			OrderTypes.Limit when triggerPrice is > 0 => FyersApiOrderTypes.StopLimit,
			OrderTypes.Limit => FyersApiOrderTypes.Limit,
			OrderTypes.Conditional when triggerPrice is > 0 => FyersApiOrderTypes.StopLimit,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unsupported FYERS order type."),
		};

	public static OrderTypes ToOrderType(this FyersApiOrderTypes orderType)
		=> orderType switch
		{
			FyersApiOrderTypes.Market => OrderTypes.Market,
			FyersApiOrderTypes.Stop or FyersApiOrderTypes.StopLimit => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this FyersOrderStatuses status)
		=> status switch
		{
			FyersOrderStatuses.Filled or FyersOrderStatuses.Cancelled or FyersOrderStatuses.Expired => OrderStates.Done,
			FyersOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static DateTime? ToFyersTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		if (!DateTime.TryParseExact(value,
			["dd-MMM-yyyy HH:mm:ss", "dd-MMM-yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;

		return DateTime.SpecifyKind(local.AddMinutes(-330), DateTimeKind.Utc);
	}

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(1))
			return "1D";
		if (timeFrame.TotalMinutes is >= 1 and <= 240 && timeFrame.TotalMinutes == Math.Truncate(timeFrame.TotalMinutes))
			return ((int)timeFrame.TotalMinutes).ToString(CultureInfo.InvariantCulture);
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported FYERS candle interval.");
	}
}
