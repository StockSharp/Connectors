namespace StockSharp.Aster.Native;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromDays(30), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string name)
		=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string secCode, string boardCode)
	{
		if (secCode.IsEmpty())
			throw new ArgumentNullException(nameof(secCode));

		if (boardCode.IsEmpty())
			throw new ArgumentNullException(nameof(boardCode));

		return new()
		{
			SecurityCode = secCode.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}

	public static string ToNative(this SecurityId securityId)
		=> securityId.SecurityCode;

	public static string ToNative(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"BUY" or "BID" or "LONG" => Sides.Buy,
			"SELL" or "ASK" or "SHORT" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this TimeInForce? tif, bool? postOnly)
	{
		if (postOnly == true)
			return "GTX";

		return tif switch
		{
			null or TimeInForce.PutInQueue => "GTC",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif, out bool? postOnly)
	{
		postOnly = null;

		switch (tif?.ToUpperInvariant())
		{
			case null:
				return null;

			case "GTX":
				postOnly = true;
				return TimeInForce.PutInQueue;

			case "GTC":
			case "GTE_GTC":
				return TimeInForce.PutInQueue;

			case "IOC":
				return TimeInForce.CancelBalance;

			case "FOK":
				return TimeInForce.MatchOrCancel;

			default:
				return null;
		}
	}

	public static OrderStates ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"NEW" or "PARTIALLY_FILLED" or "PENDING_CANCEL" => OrderStates.Active,
			"FILLED" or "CANCELED" or "EXPIRED" or "REPLACED" or "STOPPED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static OrderTypes? ToOrderType(this string type, out bool? postOnly, out AsterOrderCondition condition)
	{
		postOnly = null;
		condition = null;

		switch (type?.ToUpperInvariant())
		{
			case null:
				return null;
			case "LIMIT_MAKER":
				postOnly = true;
				return OrderTypes.Limit;
			case "LIMIT":
				return OrderTypes.Limit;
			case "MARKET":
				return OrderTypes.Market;
			case "STOP":
			case "STOP_LOSS":
			case "STOP_LOSS_LIMIT":
			case "STOP_MARKET":
				condition = new AsterOrderCondition { Type = AsterOrderConditionTypes.StopLoss };
				return OrderTypes.Conditional;
			case "TAKE_PROFIT":
			case "TAKE_PROFIT_LIMIT":
			case "TAKE_PROFIT_MARKET":
				condition = new AsterOrderCondition { Type = AsterOrderConditionTypes.TakeProfit };
				return OrderTypes.Conditional;
			default:
				return OrderTypes.Limit;
		}
	}

	public static string ExtractBaseAsset(string symbol)
	{
		if (symbol.IsEmpty())
			return symbol;

		foreach (var quote in new[] { "USDT", "USDC", "BUSD", "BTC", "ETH" })
		{
			if (symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase) && symbol.Length > quote.Length)
				return symbol[..^quote.Length];
		}

		return symbol;
	}
}
