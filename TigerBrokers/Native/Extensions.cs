namespace StockSharp.TigerBrokers.Native;

static class TigerExtensions
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(45),
		TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(4),
		TimeSpan.FromHours(6), TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static License ToNative(this TigerLicenses license)
		=> license switch
		{
			TigerLicenses.NewZealand => License.TBNZ,
			TigerLicenses.Singapore => License.TBSG,
			TigerLicenses.Australia => License.TBAU,
			TigerLicenses.HongKong => License.TBHK,
			_ => throw new ArgumentOutOfRangeException(nameof(license), license, null),
		};

	public static string ToBoardCode(this Market market)
		=> market switch
		{
			Market.US => "TIGER_US",
			Market.HK => "TIGER_HK",
			Market.CN => "TIGER_CN",
			Market.SG => "TIGER_SG",
			Market.AU => "TIGER_AU",
			Market.NZ => "TIGER_NZ",
			Market.UK => "TIGER_UK",
			_ => "TIGER_ALL",
		};

	public static Market ToMarket(this string boardCode)
		=> boardCode?.ToUpperInvariant() switch
		{
			"TIGER_HK" or "SEHK" => Market.HK,
			"TIGER_CN" or "SSE" or "SZSE" => Market.CN,
			"TIGER_SG" or "SGX" => Market.SG,
			"TIGER_AU" or "ASX" => Market.AU,
			"TIGER_NZ" or "NZX" => Market.NZ,
			"TIGER_UK" or "LSE" => Market.UK,
			_ => Market.US,
		};

	public static SecurityTypes ToSecurityType(this SecType securityType)
		=> securityType switch
		{
			SecType.OPT or SecType.FOP => SecurityTypes.Option,
			SecType.FUT => SecurityTypes.Future,
			SecType.FUND => SecurityTypes.Fund,
			SecType.CASH or SecType.FOREX => SecurityTypes.Currency,
			SecType.WAR or SecType.IOPT => SecurityTypes.Warrant,
			_ => SecurityTypes.Stock,
		};

	public static SecType ToNative(this SecurityTypes securityType)
		=> securityType switch
		{
			SecurityTypes.Option => SecType.OPT,
			SecurityTypes.Future => SecType.FUT,
			SecurityTypes.Fund => SecType.FUND,
			SecurityTypes.Currency => SecType.CASH,
			SecurityTypes.Warrant => SecType.WAR,
			_ => SecType.STK,
		};

	public static SecurityId ToSecurityId(this TigerInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.SubscriptionSymbol.IsEmpty(instrument.Symbol),
			BoardCode = instrument.Exchange.IsEmpty() ? instrument.Market.ToBoardCode() : instrument.Exchange,
			Native = instrument.SubscriptionSymbol.IsEmpty(instrument.Symbol),
		};

	public static TigerFeedTypes ToFeedType(this TigerInstrument instrument)
		=> instrument.SecurityType switch
		{
			SecType.OPT or SecType.FOP => TigerFeedTypes.Option,
			SecType.FUT => TigerFeedTypes.Future,
			_ => TigerFeedTypes.Quote,
		};

	public static TigerInstrument ToInstrument(this FutureContractItem future)
		=> new()
		{
			Symbol = future.ContractCode,
			SubscriptionSymbol = future.ContractCode,
			Name = future.Name,
			SecurityType = SecType.FUT,
			Market = Market.ALL,
			Exchange = future.ExchangeCode.IsEmpty(future.Exchange),
			Currency = future.Currency,
			ExpiryDate = future.LastTradingDate.ParseTigerDate(),
			Multiplier = future.Multiplier > 0 ? future.Multiplier : null,
			PriceStep = future.MinTick > 0 ? future.MinTick : null,
		};

	public static TigerInstrument ToInstrument(this OptionRealTimeQuote option, string underlying, Market market, long expiry)
	{
		var expiryDate = expiry.FromUnixMilliseconds();
		var strike = decimal.TryParse(option.Strike, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedStrike)
			? parsedStrike : (decimal?)null;
		var subscriptionSymbol = option.Identifier.IsEmpty()
			? $"{underlying} {expiryDate:yyyyMMdd} {option.Strike} {option.Right}"
			: option.Identifier;
		return new()
		{
			Symbol = underlying,
			SubscriptionSymbol = subscriptionSymbol,
			Name = subscriptionSymbol,
			SecurityType = SecType.OPT,
			Market = market,
			ExpiryDate = expiryDate,
			Strike = strike,
			Right = option.Right,
			Multiplier = option.Multiplier > 0 ? option.Multiplier : null,
		};
	}

	public static TigerInstrument ToInstrument(this SecurityId securityId)
	{
		var symbol = (securityId.Native as string).IsEmpty(securityId.SecurityCode);
		if (symbol.IsEmpty())
			throw new InvalidOperationException("Tiger security symbol is missing.");
		var parts = symbol.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 4 && DateTime.TryParseExact(parts[^3], "yyyyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out var expiry) &&
			decimal.TryParse(parts[^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var strike) &&
			parts[^1].ToUpperInvariant() is "CALL" or "PUT")
		{
			return new()
			{
				Symbol = parts[..^3].Join(" "),
				SubscriptionSymbol = symbol,
				SecurityType = SecType.OPT,
				Market = securityId.BoardCode.ToMarket(),
				ExpiryDate = DateTime.SpecifyKind(expiry, DateTimeKind.Utc),
				Strike = strike,
				Right = parts[^1].ToUpperInvariant(),
			};
		}
		return new()
		{
			Symbol = symbol,
			SubscriptionSymbol = symbol,
			SecurityType = securityId.BoardCode.StartsWithIgnoreCase("TIGER_") ? SecType.STK : SecType.FUT,
			Market = securityId.BoardCode.ToMarket(),
			Exchange = securityId.BoardCode.StartsWithIgnoreCase("TIGER_") ? null : securityId.BoardCode,
		};
	}

	public static QuoteChange[] ToQuotes(this QuoteDepthData.Types.OrderBook book, bool bids)
	{
		if (book == null)
			return [];
		var count = Math.Min(book.Price.Count, book.Volume.Count);
		var quotes = new List<QuoteChange>(count);
		for (var i = 0; i < count; i++)
		{
			if (book.Price[i] <= 0)
				continue;
			quotes.Add(new((decimal)book.Price[i], book.Volume[i])
			{
				OrdersCount = i < book.OrderCount.Count ? checked((int)book.OrderCount[i]) : null,
			});
		}
		return [.. (bids ? quotes.OrderByDescending(q => q.Price) : quotes.OrderBy(q => q.Price))];
	}

	public static TigerTradeSession ToNative(this TigerSessions session)
		=> session switch
		{
			TigerSessions.PreMarket => TigerTradeSession.PreMarket,
			TigerSessions.AfterHours => TigerTradeSession.AfterHours,
			TigerSessions.Overnight => TigerTradeSession.OverNight,
			TigerSessions.All => TigerTradeSession.All,
			_ => TigerTradeSession.Regular,
		};

	public static TigerSessions ToSession(this string session)
		=> session?.ToUpperInvariant() switch
		{
			"PREMARKET" => TigerSessions.PreMarket,
			"AFTERHOURS" => TigerSessions.AfterHours,
			"OVERNIGHT" => TigerSessions.Overnight,
			"ALL" => TigerSessions.All,
			_ => TigerSessions.Regular,
		};

	public static TigerAction ToNative(this Sides side)
		=> side == Sides.Buy ? TigerAction.BUY : TigerAction.SELL;

	public static Sides ToSide(this string action)
		=> action.EqualsIgnoreCase(nameof(TigerAction.BUY)) ? Sides.Buy : Sides.Sell;

	public static TigerOrderType ToNative(this OrderTypes orderType, TigerBrokersOrderCondition condition)
	{
		if (condition?.TrailingPercent is > 0)
			return TigerOrderType.TRAIL;
		if (condition?.StopPrice is > 0)
			return orderType == OrderTypes.Market ? TigerOrderType.STP : TigerOrderType.STP_LMT;
		return orderType == OrderTypes.Market ? TigerOrderType.MKT : TigerOrderType.LMT;
	}

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MKT" or "STP" => OrderTypes.Market,
			"STP_LMT" or "TRAIL" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TigerTimeInForce ToNative(this TimeInForce? timeInForce, DateTimeOffset? tillDate)
		=> tillDate is not null ? TigerTimeInForce.GTD : timeInForce switch
		{
			TimeInForce.CancelBalance => TigerTimeInForce.DAY,
			TimeInForce.MatchOrCancel => TigerTimeInForce.DAY,
			_ => TigerTimeInForce.DAY,
		};

	public static TimeInForce ToTimeInForce(this string timeInForce)
		=> timeInForce?.ToUpperInvariant() switch
		{
			"GTC" or "GTD" => TimeInForce.PutInQueue,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this string status)
		=> status?.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
		{
			"FILLED" or "CANCELLED" => OrderStates.Done,
			"INVALID" or "INACTIVE" => OrderStates.Failed,
			"INITIAL" or "PENDINGSUBMIT" or "PENDINGCANCEL" => OrderStates.Pending,
			_ => OrderStates.Active,
		};

	public static CurrencyTypes? ToCurrency(this string currency)
		=> Enum.TryParse<CurrencyTypes>(currency, true, out var result) ? result : null;

	public static TigerCurrency ToTigerCurrency(this string currency)
		=> Enum.TryParse<TigerCurrency>(currency, true, out var result) ? result : TigerCurrency.NONE;

	public static DateTime FromUnixMilliseconds(this long value)
	{
		if (value <= 0)
			return DateTime.UtcNow;
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return DateTime.UtcNow;
		}
	}

	public static long ToUnixMilliseconds(this DateTimeOffset? value)
		=> value?.ToUniversalTime().ToUnixTimeMilliseconds() ?? 0;

	public static long ToUnixMilliseconds(this DateTime? value)
		=> value is null ? 0 : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

	public static DateTime? ParseTigerDate(this string value)
	{
		if (value.IsEmpty())
			return null;
		return DateTime.TryParseExact(value.Replace("-", string.Empty, StringComparison.Ordinal), "yyyyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
			? DateTime.SpecifyKind(result, DateTimeKind.Utc)
			: null;
	}

	public static (long quantity, int scale) ToScaledQuantity(this decimal value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value), value, "Order volume must be positive.");
		var scale = 0;
		var quantity = value;
		while (quantity != decimal.Truncate(quantity) && scale < 6)
		{
			quantity *= 10;
			scale++;
		}
		if (quantity != decimal.Truncate(quantity) || quantity > long.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value), value, "Order volume has unsupported precision.");
		return ((long)quantity, scale);
	}

	public static decimal FromScaledQuantity(this long value, int scale)
	{
		var result = (decimal)value;
		for (var i = 0; i < scale; i++)
			result /= 10m;
		return result;
	}

	public static string ToStockPeriod(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => KLineType.min1.Value,
			{ TotalMinutes: 3 } => KLineType.min3.Value,
			{ TotalMinutes: 5 } => KLineType.min5.Value,
			{ TotalMinutes: 15 } => KLineType.min15.Value,
			{ TotalMinutes: 30 } => KLineType.min30.Value,
			{ TotalHours: 1 } => KLineType.min60.Value,
			{ TotalHours: 2 } => KLineType.min120.Value,
			{ TotalHours: 4 } => KLineType.min240.Value,
			{ TotalDays: 1 } => KLineType.day.Value,
			{ TotalDays: 7 } => KLineType.week.Value,
			_ when timeFrame >= TimeSpan.FromDays(28) && timeFrame <= TimeSpan.FromDays(31) => KLineType.month.Value,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Tiger stock candle time frame."),
		};

	public static string ToFuturePeriod(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => FutureKType.min1.Value,
			{ TotalMinutes: 2 } => FutureKType.min2.Value,
			{ TotalMinutes: 3 } => FutureKType.min3.Value,
			{ TotalMinutes: 5 } => FutureKType.min5.Value,
			{ TotalMinutes: 10 } => FutureKType.min10.Value,
			{ TotalMinutes: 15 } => FutureKType.min15.Value,
			{ TotalMinutes: 30 } => FutureKType.min30.Value,
			{ TotalMinutes: 45 } => FutureKType.min45.Value,
			{ TotalHours: 1 } => FutureKType.min60.Value,
			{ TotalHours: 2 } => FutureKType.hour2.Value,
			{ TotalHours: 3 } => FutureKType.hour3.Value,
			{ TotalHours: 4 } => FutureKType.hour4.Value,
			{ TotalHours: 6 } => FutureKType.hour6.Value,
			{ TotalDays: 1 } => FutureKType.day.Value,
			{ TotalDays: 7 } => FutureKType.week.Value,
			_ when timeFrame >= TimeSpan.FromDays(28) && timeFrame <= TimeSpan.FromDays(31) => FutureKType.month.Value,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Tiger futures candle time frame."),
		};

	public static string ToOptionPeriod(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => OptionKType.min1.Value,
			{ TotalMinutes: 5 } => OptionKType.min5.Value,
			{ TotalMinutes: 30 } => OptionKType.min30.Value,
			{ TotalHours: 1 } => OptionKType.min60.Value,
			{ TotalDays: 1 } => OptionKType.day.Value,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Tiger option candle time frame."),
		};
}
