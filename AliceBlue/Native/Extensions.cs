namespace StockSharp.AliceBlue.Native;

static class Extensions
{
	private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

	public static string ToInstrumentKey(this string exchange, string instrumentId)
		=> $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}|{instrumentId.ThrowIfEmpty(nameof(instrumentId))}";

	public static (string exchange, string instrumentId) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || parts[0].IsEmpty() || parts[1].IsEmpty())
			throw new FormatException($"Invalid Alice Blue instrument key '{key}'.");
		parts[0].ToBoardCode();
		return (parts[0].ToUpperInvariant(), parts[1]);
	}

	public static string ToInstrumentKey(this SecurityId securityId)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			native.ParseInstrumentKey();
			return native;
		}
		if (securityId.SecurityCode?.Split('|') is { Length: 2 })
		{
			securityId.SecurityCode.ParseInstrumentKey();
			return securityId.SecurityCode;
		}
		throw new InvalidOperationException("Alice Blue instrument ID is missing. Select the security through ANT lookup so SecurityId.Native contains exchange|instrumentId.");
	}

	public static string ToBoardCode(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NSE" => "NSE",
			"BSE" => "BSE",
			"NFO" => "NFO",
			"BFO" => "BFO",
			"CDS" => "CDS",
			"BCD" => "BCD",
			"MCX" => "MCX",
			_ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unsupported Alice Blue exchange segment."),
		};

	public static SecurityId ToSecurityId(this AliceBlueInstrument instrument)
		=> instrument.Exchange.ToSecurityId(instrument.Token, instrument.TradingSymbol.IsEmpty(instrument.Symbol));

	public static SecurityId ToSecurityId(this string exchange, string instrumentId, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty(instrumentId),
			BoardCode = exchange.ToBoardCode(),
			Native = exchange.ToInstrumentKey(instrumentId),
		};

	public static SecurityTypes ToSecurityType(this AliceBlueInstrument instrument)
	{
		if (instrument.IsIndex)
			return SecurityTypes.Index;
		var type = instrument.InstrumentType?.ToUpperInvariant();
		if (type?.Contains("OPT", StringComparison.Ordinal) == true || type is "SO" or "IO" ||
			instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true || type is "SF" or "IF")
			return SecurityTypes.Future;
		if (instrument.Exchange.EqualsIgnoreCase("CDS") || instrument.Exchange.EqualsIgnoreCase("BCD") ||
			type?.Contains("CUR", StringComparison.Ordinal) == true)
			return SecurityTypes.Currency;
		if (instrument.Exchange.EqualsIgnoreCase("MCX"))
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.ToUpperInvariant() switch
		{
			"CE" or "C" => OptionTypes.Call,
			"PE" or "P" => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ToExpiry(this long? milliseconds)
	{
		if (milliseconds is not > 0)
			return null;
		try
		{
			return DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(milliseconds.Value), DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	public static string ToNative(this AliceBlueProducts product)
		=> product switch
		{
			AliceBlueProducts.LongTerm => "LONGTERM",
			AliceBlueProducts.Intraday => "INTRADAY",
			AliceBlueProducts.Mtf => "MTF",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static AliceBlueProducts ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"INTRADAY" or "MIS" or "I" => AliceBlueProducts.Intraday,
			"MTF" => AliceBlueProducts.Mtf,
			_ => AliceBlueProducts.LongTerm,
		};

	public static string ToNative(this AliceBlueOrderComplexities complexity)
		=> complexity switch
		{
			AliceBlueOrderComplexities.Regular => "REGULAR",
			AliceBlueOrderComplexities.AfterMarket => "AMO",
			AliceBlueOrderComplexities.Cover => "CO",
			AliceBlueOrderComplexities.Bracket => "BO",
			_ => throw new ArgumentOutOfRangeException(nameof(complexity), complexity, null),
		};

	public static AliceBlueOrderComplexities ToComplexity(this string complexity)
		=> complexity?.ToUpperInvariant() switch
		{
			"AMO" => AliceBlueOrderComplexities.AfterMarket,
			"CO" => AliceBlueOrderComplexities.Cover,
			"BO" => AliceBlueOrderComplexities.Bracket,
			_ => AliceBlueOrderComplexities.Regular,
		};

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("B") ? Sides.Buy : Sides.Sell;

	public static string ToNative(this OrderTypes orderType, decimal price)
		=> orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when price > 0 => "SL",
			OrderTypes.Conditional => "SLM",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Alice Blue supports market, limit, stop-limit, and stop-market orders."),
		};

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" or "MKT" => OrderTypes.Market,
			"SL" or "SLM" or "SL-LMT" or "SL-MKT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToValidity(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

	public static TimeInForce ToTimeInForce(this string validity)
		=> validity.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static OrderStates ToOrderState(this string status, string reportType = null)
	{
		var value = status?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		var report = reportType?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		if (value is "REJECTED" or "REJECT" or "FAILED" ||
			report is "REJECTED" or "REPLACEREJECTED" or "CANCELREJECTED")
			return OrderStates.Failed;
		if (value is "CANCELED" or "CANCELLED" or "COMPLETE" or "COMPLETED" ||
			report == "CANCELED")
			return OrderStates.Done;
		if (value is "PENDING" or "TRIGGERPENDING" || report?.StartsWith("PENDING", StringComparison.Ordinal) == true)
			return OrderStates.Pending;
		return OrderStates.Active;
	}

	public static decimal ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;

	public static int ToInt(this string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

	public static DateTime? ToAliceBlueTime(this string value)
	{
		if (value.IsEmpty() || value.Trim() is "0" or "-")
			return null;
		var text = value.Trim();
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
		{
			try
			{
				return DateTime.SpecifyKind(epoch > 100000000000
					? DateTime.UnixEpoch.AddMilliseconds(epoch)
					: DateTime.UnixEpoch.AddSeconds(epoch), DateTimeKind.Utc);
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}
		if (DateTime.TryParseExact(text,
			["yyyy-MM-dd HH:mm:ss", "dd-MMM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm:ss",
				"HH:mm:ss dd-MM-yyyy", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HHmm",
				"yyyy-MM-dd HH:mm"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return ToUtcFromIndia(local);
		if (DateTime.TryParseExact(text, ["HH:mm:ss", "HH:mm"], CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var time))
		{
			var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _indiaTimeZone);
			return ToUtcFromIndia(indiaNow.Date.Add(time.TimeOfDay));
		}
		return null;
	}

	public static long ToUnixMilliseconds(this DateTime value)
	{
		var utc = value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
		return Convert.ToInt64(Math.Floor((utc - DateTime.UnixEpoch).TotalMilliseconds));
	}

	public static string DoubleSha256(this string value)
	{
		var first = SHA256.HashData(Encoding.UTF8.GetBytes(value.ThrowIfEmpty(nameof(value))));
		var second = SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexString(first).ToLowerInvariant()));
		return Convert.ToHexString(second).ToLowerInvariant();
	}

	public static DateTime GetMarketTime(this AliceBlueMarketUpdate update)
		=> update.FeedTime.ToAliceBlueTime() ?? update.LastTradeTime.ToAliceBlueTime() ?? DateTime.UtcNow;

	public static void Apply(this AliceBlueMarketUpdate state, AliceBlueMarketUpdate update)
	{
		state.Type = update.Type ?? state.Type;
		state.Exchange = update.Exchange ?? state.Exchange;
		state.Token = update.Token ?? state.Token;
		state.TradingSymbol = update.TradingSymbol ?? state.TradingSymbol;
		state.Precision = update.Precision ?? state.Precision;
		state.TickSize = update.TickSize ?? state.TickSize;
		state.LotSize = update.LotSize ?? state.LotSize;
		state.LastPrice = update.LastPrice ?? state.LastPrice;
		state.LastQuantity = update.LastQuantity ?? state.LastQuantity;
		state.LastTradeTime = update.LastTradeTime ?? state.LastTradeTime;
		state.Volume = update.Volume ?? state.Volume;
		state.Open = update.Open ?? state.Open;
		state.High = update.High ?? state.High;
		state.Low = update.Low ?? state.Low;
		state.Close = update.Close ?? state.Close;
		state.AveragePrice = update.AveragePrice ?? state.AveragePrice;
		state.OpenInterest = update.OpenInterest ?? state.OpenInterest;
		state.TotalBuyQuantity = update.TotalBuyQuantity ?? state.TotalBuyQuantity;
		state.TotalSellQuantity = update.TotalSellQuantity ?? state.TotalSellQuantity;
		state.LowerCircuit = update.LowerCircuit ?? state.LowerCircuit;
		state.UpperCircuit = update.UpperCircuit ?? state.UpperCircuit;
		state.YearHigh = update.YearHigh ?? state.YearHigh;
		state.YearLow = update.YearLow ?? state.YearLow;
		state.FeedTime = update.FeedTime ?? state.FeedTime;
		state.BidPrice1 = update.BidPrice1 ?? state.BidPrice1;
		state.BidQuantity1 = update.BidQuantity1 ?? state.BidQuantity1;
		state.BidOrders1 = update.BidOrders1 ?? state.BidOrders1;
		state.AskPrice1 = update.AskPrice1 ?? state.AskPrice1;
		state.AskQuantity1 = update.AskQuantity1 ?? state.AskQuantity1;
		state.AskOrders1 = update.AskOrders1 ?? state.AskOrders1;
		state.BidPrice2 = update.BidPrice2 ?? state.BidPrice2;
		state.BidQuantity2 = update.BidQuantity2 ?? state.BidQuantity2;
		state.BidOrders2 = update.BidOrders2 ?? state.BidOrders2;
		state.AskPrice2 = update.AskPrice2 ?? state.AskPrice2;
		state.AskQuantity2 = update.AskQuantity2 ?? state.AskQuantity2;
		state.AskOrders2 = update.AskOrders2 ?? state.AskOrders2;
		state.BidPrice3 = update.BidPrice3 ?? state.BidPrice3;
		state.BidQuantity3 = update.BidQuantity3 ?? state.BidQuantity3;
		state.BidOrders3 = update.BidOrders3 ?? state.BidOrders3;
		state.AskPrice3 = update.AskPrice3 ?? state.AskPrice3;
		state.AskQuantity3 = update.AskQuantity3 ?? state.AskQuantity3;
		state.AskOrders3 = update.AskOrders3 ?? state.AskOrders3;
		state.BidPrice4 = update.BidPrice4 ?? state.BidPrice4;
		state.BidQuantity4 = update.BidQuantity4 ?? state.BidQuantity4;
		state.BidOrders4 = update.BidOrders4 ?? state.BidOrders4;
		state.AskPrice4 = update.AskPrice4 ?? state.AskPrice4;
		state.AskQuantity4 = update.AskQuantity4 ?? state.AskQuantity4;
		state.AskOrders4 = update.AskOrders4 ?? state.AskOrders4;
		state.BidPrice5 = update.BidPrice5 ?? state.BidPrice5;
		state.BidQuantity5 = update.BidQuantity5 ?? state.BidQuantity5;
		state.BidOrders5 = update.BidOrders5 ?? state.BidOrders5;
		state.AskPrice5 = update.AskPrice5 ?? state.AskPrice5;
		state.AskQuantity5 = update.AskQuantity5 ?? state.AskQuantity5;
		state.AskOrders5 = update.AskOrders5 ?? state.AskOrders5;
	}

	public static AliceBlueDepthLevel[] GetBids(this AliceBlueMarketUpdate state)
		=> CreateDepth(
			(state.BidPrice1, state.BidQuantity1, state.BidOrders1),
			(state.BidPrice2, state.BidQuantity2, state.BidOrders2),
			(state.BidPrice3, state.BidQuantity3, state.BidOrders3),
			(state.BidPrice4, state.BidQuantity4, state.BidOrders4),
			(state.BidPrice5, state.BidQuantity5, state.BidOrders5));

	public static AliceBlueDepthLevel[] GetAsks(this AliceBlueMarketUpdate state)
		=> CreateDepth(
			(state.AskPrice1, state.AskQuantity1, state.AskOrders1),
			(state.AskPrice2, state.AskQuantity2, state.AskOrders2),
			(state.AskPrice3, state.AskQuantity3, state.AskOrders3),
			(state.AskPrice4, state.AskQuantity4, state.AskOrders4),
			(state.AskPrice5, state.AskQuantity5, state.AskOrders5));

	private static AliceBlueDepthLevel[] CreateDepth(params (string price, string volume, string orders)[] values)
		=> [.. values.Select(value => new AliceBlueDepthLevel
			{
				Price = value.price.ToDecimal(),
				Volume = value.volume.ToDecimal(),
				OrdersCount = value.orders.ToInt(),
			})
			.Where(level => level.Price > 0)];

	private static DateTime ToUtcFromIndia(DateTime local)
		=> TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaTimeZone);

	private static TimeZoneInfo GetIndiaTimeZone()
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
		}
		catch (TimeZoneNotFoundException)
		{
			return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
		}
	}
}
