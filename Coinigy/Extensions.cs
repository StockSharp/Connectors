namespace StockSharp.Coinigy;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		switch (side)
		{
			case Sides.Buy:
				return "Buy";
			case Sides.Sell:
				return "Sell";
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static Sides ToSide(this string side)
	{
		switch (side?.ToLowerInvariant())
		{
			case "buy":
				return Sides.Buy;
			case "sell":
				return Sides.Sell;
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "m" },
		{ TimeSpan.FromHours(1), "h" },
		{ TimeSpan.FromDays(1), "d" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		var name = TimeFrames.TryGetValue(timeFrame);

		if (name == null)
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return name;
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		var timeFrame = TimeFrames.TryGetKey2(name);

		if (timeFrame == null)
			throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

		return timeFrame.Value;
	}

	private static readonly PairSet<string, string> _boards = new()
	{
		{ "BITS", BoardCodes.BitStamp },
		{ "BITF", BoardCodes.Bitfinex },
		{ "KRKN", BoardCodes.Kraken },
		{ "PLNX", BoardCodes.Poloniex },
		{ "BTRX", BoardCodes.Bittrex },
		//{ "CCEX", BoardCodes.CCex },
		{ "HITB", BoardCodes.HitBtc },
		//{ "OKFT", BoardCodes.OKCoinFutures },
		//{ "LAKE", BoardCodes.LakeBTC },
		//{ "QCX", BoardCodes.QuadrigaCX },
		{ "CXIO", BoardCodes.Cex },
		{ "BMEX", BoardCodes.Bitmex },
		//{ "GMNI", BoardCodes.Gemini },
		//{ "ITBT", BoardCodes.ItBit },
		//{ "ROCK", BoardCodes.TheRock },
		//{ "GATE", BoardCodes.Gatecoin },
		//{ "BIND", BoardCodes.Indodax },
		//{ "BXTH", BoardCodes.BitcoinExchangeThailand },
		//{ "MATE", BoardCodes.Coinmate },
		//{ "BLEU", BoardCodes.Bleutrade },
		//{ "VWOX", BoardCodes.Virwox },
		{ "YOBT", BoardCodes.Yobit },
		//{ "GOLD", BoardCodes.Vaultoro },
		{ "CPIA", BoardCodes.Cryptopia },
		//{ "MERC", BoardCodes.MercadoBitcoin },
		//{ "KBIT", BoardCodes.Korbit },
		//{ "CBNK", BoardCodes.Coinsbank },
		{ "EXMO", BoardCodes.Exmo },
		{ "CCJP", BoardCodes.Coincheck },
		{ "GDAX", BoardCodes.Gdax },
		//{ "PAYM", BoardCodes.Paymium },
		{ "LIQU", BoardCodes.Liqui },
		//{ "BTCM", BoardCodes.BTCMarkets },
		{ "LIVE", BoardCodes.LiveCoin },
		//{ "FLYR", BoardCodes.BitFlyer },
		{ "BINA", BoardCodes.Binance },
		{ "OKEX", BoardCodes.Okex },
		{ "BTHM", BoardCodes.Bithumb },
		//{ "CONE", BoardCodes.Coinone },
		{ "KUCN", BoardCodes.Kucoin },
		{ "HUBI", BoardCodes.Huobi },
		{ "DERI", BoardCodes.Deribit },
	};

	public static string ToBoardCode(this string exchCode)
	{
		return _boards.TryGetValue(exchCode, out var boardCode) ? boardCode : exchCode;
	}

	public static bool IsSupportedExchange(this string boardCode)
	{
		return _boards.ContainsValue(boardCode);
	}

	public static void ToCurrency(this SecurityId securityId, out string baseCurr, out string quoteCurr, out string exchange)
	{
		exchange = _boards.TryGetKey(securityId.BoardCode, out var boardCode) ? boardCode : securityId.BoardCode;

		var parts = securityId.SecurityCode.Split('/');

		baseCurr = parts[0];
		quoteCurr = parts[1];
	}

	public static SecurityId ToStockSharp(this string currency, string exchCode)
	{
		return new SecurityId
		{
			SecurityCode = currency.Insert(currency.Length - 3, "/").ToUpperInvariant(),
			BoardCode = exchCode.ToBoardCode(),
		};
	}

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return order.QuantityRemaining?.ToDecimal() ?? 0;
	}

	public static OrderTypes? ToOrderType(this string orderPriceType)
	{
		if (orderPriceType.IsEmpty())
			return null;

		switch (orderPriceType)
		{
			case "Limit":
			case "LimitMargin":
				return OrderTypes.Limit;

			case "Market":
				return OrderTypes.Market;
			
			case "StopMarket":
			case "StopLimit":
			case "TrailingStopLimit":
			case "StopLimitMargin":
				return OrderTypes.Conditional;

			default:
				throw new ArgumentOutOfRangeException(nameof(orderPriceType), orderPriceType, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderStates? ToOrderState(this string status)
	{
		if (status.IsEmpty())
			return null;

		switch (status)
		{
			case "PlacingOrder":
				return OrderStates.Pending;

			case "Open":
			case "CancelFailed":
			case "Cancelling":
			case "Active":
				return OrderStates.Active;

			case "Executed":
			case "Cancelled":
			case "Stopped":
			case "NSF":
				return OrderStates.Done;

			case "Failed":
			case "ExceededOrderLimit":
				return OrderStates.Failed;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}
}