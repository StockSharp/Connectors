namespace StockSharp.Shoonya.Native;

static class Extensions
{
	private static readonly TimeZoneInfo _indiaTimeZone = GetIndiaTimeZone();

	public static string ToInstrumentKey(this string exchange, string token)
		=> $"{exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant()}|{token.ThrowIfEmpty(nameof(token))}";

	public static (string exchange, string token) ParseInstrumentKey(this string key)
	{
		var parts = key?.Split('|');
		if (parts?.Length != 2 || parts[0].IsEmpty() || parts[1].IsEmpty())
			throw new FormatException($"Invalid Shoonya instrument key '{key}'.");
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

		throw new InvalidOperationException("Shoonya token is missing. Select the security through Shoonya lookup so SecurityId.Native contains exchange|token.");
	}

	public static string ToBoardCode(this string exchange)
		=> exchange?.ToUpperInvariant() switch
		{
			"NSE" => "NSE",
			"BSE" => "BSE",
			"NFO" => "NFO",
			"BFO" => "BFO",
			"CDS" => "CDS",
			"MCX" => "MCX",
			_ => throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unsupported Shoonya exchange segment."),
		};

	public static SecurityId ToSecurityId(this ShoonyaInstrument instrument)
		=> instrument.Exchange.ToSecurityId(instrument.Token, instrument.TradingSymbol.IsEmpty(instrument.Symbol));

	public static SecurityId ToSecurityId(this string exchange, string token, string symbol = null)
		=> new()
		{
			SecurityCode = symbol.IsEmpty(token),
			BoardCode = exchange.ToBoardCode(),
			Native = exchange.ToInstrumentKey(token),
		};

	public static SecurityTypes ToSecurityType(this ShoonyaInstrument instrument)
	{
		var type = instrument.Instrument?.ToUpperInvariant();
		if (type == "INDEX")
			return SecurityTypes.Index;
		if (type?.Contains("OPT", StringComparison.Ordinal) == true || instrument.OptionType?.ToUpperInvariant() is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (instrument.Exchange.EqualsIgnoreCase("CDS") || type?.Contains("CUR", StringComparison.Ordinal) == true)
			return SecurityTypes.Currency;
		if (instrument.Exchange.EqualsIgnoreCase("MCX") || type?.Contains("COM", StringComparison.Ordinal) == true)
			return SecurityTypes.Commodity;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CE" or "C" => OptionTypes.Call,
			"PE" or "P" => OptionTypes.Put,
			_ => null,
		};

	public static string ToNative(this ShoonyaProducts product)
		=> product switch
		{
			ShoonyaProducts.Delivery => "C",
			ShoonyaProducts.Intraday => "I",
			ShoonyaProducts.Normal => "M",
			ShoonyaProducts.Cover => "H",
			ShoonyaProducts.Bracket => "B",
			_ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
		};

	public static ShoonyaProducts ToProduct(this string product)
		=> product?.ToUpperInvariant() switch
		{
			"I" => ShoonyaProducts.Intraday,
			"M" => ShoonyaProducts.Normal,
			"H" => ShoonyaProducts.Cover,
			"B" => ShoonyaProducts.Bracket,
			_ => ShoonyaProducts.Delivery,
		};

	public static string ToNative(this Sides side)
		=> side == Sides.Buy ? "B" : "S";

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("B") || side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

	public static string ToPriceType(this OrderTypes orderType, decimal price)
		=> orderType switch
		{
			OrderTypes.Market => "MKT",
			OrderTypes.Limit => "LMT",
			OrderTypes.Conditional when price > 0 => "SL-LMT",
			OrderTypes.Conditional => "SL-MKT",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Shoonya supports market, limit, stop-limit, and stop-market orders."),
		};

	public static OrderTypes ToOrderType(this ShoonyaOrder order)
		=> order.PriceType?.ToUpperInvariant() switch
		{
			"MKT" => OrderTypes.Market,
			"SL-LMT" or "SL-MKT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToRetention(this TimeInForce? timeInForce)
		=> timeInForce == TimeInForce.CancelBalance ? "IOC" : "DAY";

	public static TimeInForce ToTimeInForce(this string retention)
		=> retention.EqualsIgnoreCase("IOC") ? TimeInForce.CancelBalance : TimeInForce.PutInQueue;

	public static OrderStates ToOrderState(this string status, string reportType)
	{
		var value = status?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		var report = reportType?.Replace("_", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
		if (value is "REJECTED" or "REJECT" or "INVALIDSTATUSTYPE" ||
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

	public static long ToLong(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0L;

	public static DateTime FromUnixSeconds(this long seconds)
	{
		if (seconds <= 0)
			return DateTime.UtcNow;
		try
		{
			return DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(seconds), DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException)
		{
			return DateTime.UtcNow;
		}
	}

	public static long ToUnixSeconds(this DateTime value)
	{
		var utc = value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
		return Convert.ToInt64(Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds));
	}

	public static DateTime? ToShoonyaTime(this string value)
	{
		if (value.IsEmpty() || value.Trim() is "0" or "-")
			return null;

		var text = value.Trim();
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 100000000)
			return seconds.FromUnixSeconds();

		if (DateTime.TryParseExact(text,
			["dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd-MMM-yyyy HH:mm:ss",
				"dd-MMM-yyyy", "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd HH:mm:ss",
				"HH:mm:ss dd-MM-yyyy", "HH:mm:ss dd/MM/yyyy"],
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

	public static DateTime? GetCandleTime(this ShoonyaCandle candle)
		=> candle.EpochTime.ToShoonyaTime() ?? candle.Time.ToShoonyaTime();

	public static DateTime GetMarketTime(this ShoonyaMarketUpdate update)
		=> update.FeedTime.ToShoonyaTime() ?? update.LastTradeTime.ToShoonyaTime() ?? DateTime.UtcNow;

	public static void Apply(this ShoonyaMarketUpdate state, ShoonyaMarketUpdate update)
	{
		Set(update.Type, value => state.Type = value);
		Set(update.Exchange, value => state.Exchange = value);
		Set(update.Token, value => state.Token = value);
		Set(update.TradingSymbol, value => state.TradingSymbol = value);
		Set(update.Precision, value => state.Precision = value);
		Set(update.TickSize, value => state.TickSize = value);
		Set(update.LotSize, value => state.LotSize = value);
		Set(update.LastPrice, value => state.LastPrice = value);
		Set(update.LastQuantity, value => state.LastQuantity = value);
		Set(update.LastTradeTime, value => state.LastTradeTime = value);
		Set(update.ChangePercent, value => state.ChangePercent = value);
		Set(update.Volume, value => state.Volume = value);
		Set(update.Open, value => state.Open = value);
		Set(update.High, value => state.High = value);
		Set(update.Low, value => state.Low = value);
		Set(update.Close, value => state.Close = value);
		Set(update.AveragePrice, value => state.AveragePrice = value);
		Set(update.OpenInterest, value => state.OpenInterest = value);
		Set(update.PreviousOpenInterest, value => state.PreviousOpenInterest = value);
		Set(update.TotalOpenInterest, value => state.TotalOpenInterest = value);
		Set(update.TotalBuyQuantity, value => state.TotalBuyQuantity = value);
		Set(update.TotalSellQuantity, value => state.TotalSellQuantity = value);
		Set(update.LowerCircuit, value => state.LowerCircuit = value);
		Set(update.UpperCircuit, value => state.UpperCircuit = value);
		Set(update.YearHigh, value => state.YearHigh = value);
		Set(update.YearLow, value => state.YearLow = value);
		Set(update.FeedTime, value => state.FeedTime = value);
		Set(update.BidPrice1, value => state.BidPrice1 = value);
		Set(update.BidQuantity1, value => state.BidQuantity1 = value);
		Set(update.BidOrders1, value => state.BidOrders1 = value);
		Set(update.AskPrice1, value => state.AskPrice1 = value);
		Set(update.AskQuantity1, value => state.AskQuantity1 = value);
		Set(update.AskOrders1, value => state.AskOrders1 = value);
		Set(update.BidPrice2, value => state.BidPrice2 = value);
		Set(update.BidQuantity2, value => state.BidQuantity2 = value);
		Set(update.BidOrders2, value => state.BidOrders2 = value);
		Set(update.AskPrice2, value => state.AskPrice2 = value);
		Set(update.AskQuantity2, value => state.AskQuantity2 = value);
		Set(update.AskOrders2, value => state.AskOrders2 = value);
		Set(update.BidPrice3, value => state.BidPrice3 = value);
		Set(update.BidQuantity3, value => state.BidQuantity3 = value);
		Set(update.BidOrders3, value => state.BidOrders3 = value);
		Set(update.AskPrice3, value => state.AskPrice3 = value);
		Set(update.AskQuantity3, value => state.AskQuantity3 = value);
		Set(update.AskOrders3, value => state.AskOrders3 = value);
		Set(update.BidPrice4, value => state.BidPrice4 = value);
		Set(update.BidQuantity4, value => state.BidQuantity4 = value);
		Set(update.BidOrders4, value => state.BidOrders4 = value);
		Set(update.AskPrice4, value => state.AskPrice4 = value);
		Set(update.AskQuantity4, value => state.AskQuantity4 = value);
		Set(update.AskOrders4, value => state.AskOrders4 = value);
		Set(update.BidPrice5, value => state.BidPrice5 = value);
		Set(update.BidQuantity5, value => state.BidQuantity5 = value);
		Set(update.BidOrders5, value => state.BidOrders5 = value);
		Set(update.AskPrice5, value => state.AskPrice5 = value);
		Set(update.AskQuantity5, value => state.AskQuantity5 = value);
		Set(update.AskOrders5, value => state.AskOrders5 = value);
	}

	public static ShoonyaDepthLevel[] GetBids(this ShoonyaMarketUpdate state)
		=> CreateDepth(
			(state.BidPrice1, state.BidQuantity1, state.BidOrders1),
			(state.BidPrice2, state.BidQuantity2, state.BidOrders2),
			(state.BidPrice3, state.BidQuantity3, state.BidOrders3),
			(state.BidPrice4, state.BidQuantity4, state.BidOrders4),
			(state.BidPrice5, state.BidQuantity5, state.BidOrders5));

	public static ShoonyaDepthLevel[] GetAsks(this ShoonyaMarketUpdate state)
		=> CreateDepth(
			(state.AskPrice1, state.AskQuantity1, state.AskOrders1),
			(state.AskPrice2, state.AskQuantity2, state.AskOrders2),
			(state.AskPrice3, state.AskQuantity3, state.AskOrders3),
			(state.AskPrice4, state.AskQuantity4, state.AskOrders4),
			(state.AskPrice5, state.AskQuantity5, state.AskOrders5));

	private static ShoonyaDepthLevel[] CreateDepth(params (string price, string volume, string orders)[] values)
		=> [.. values
			.Select(v => new ShoonyaDepthLevel
			{
				Price = v.price.ToDecimal(),
				Volume = v.volume.ToDecimal(),
				OrdersCount = v.orders.ToInt(),
			})
			.Where(v => v.Price > 0)];

	private static void Set(string value, Action<string> setter)
	{
		if (value != null)
			setter(value);
	}

	public static DateTime ToUtcFromIndia(this DateTime local)
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
