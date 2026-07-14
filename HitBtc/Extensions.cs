namespace StockSharp.HitBtc;

static class Extensions
{
	private const string _orderIdPrefix = "stocksharp_";

	public static string ToClientOrderId(this long transactionId)
	{
		if (transactionId == 0)
			throw new ArgumentOutOfRangeException(nameof(transactionId));

		return _orderIdPrefix + transactionId;
	}

	public static long? TryToTransactionId(this string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return null;

		if (clientOrderId.StartsWithIgnoreCase(_orderIdPrefix))
			clientOrderId = clientOrderId.Remove(_orderIdPrefix, true);

		if (long.TryParse(clientOrderId, out var transactionId))
			return transactionId;

		return null;
	}

	public static string ToNative(this OrderTypes? type, decimal? stopPrice)
	{
		switch (type)
		{
			case null:
			case OrderTypes.Limit:
				return "limit";
			case OrderTypes.Market:
				return "market";
			case OrderTypes.Conditional:
				return stopPrice == null ? "stopMarket" : "stopLimit";
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderTypes ToOrderType(this string type, double? stopPrice, out HitBtcOrderCondition condition)
	{
		condition = null;

		switch (type)
		{
			case "limit":
				return OrderTypes.Limit;
			case "market":
				return OrderTypes.Market;
			case "stopMarket":
			case "stopLimit":
				condition = new HitBtcOrderCondition
				{
					StopPrice = stopPrice?.ToDecimal(),
				};
				return OrderTypes.Conditional;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this Sides side)
	{
		switch (side)
		{
			case Sides.Buy:
				return "buy";
			case Sides.Sell:
				return "sell";
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static Sides ToSide(this string side)
	{
		switch (side)
		{
			case "buy":
				return Sides.Buy;
			case "sell":
				return Sides.Sell;
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this TimeInForce? tif, DateTime? tillDate, OrderTypes? type)
	{
		switch (tif)
		{
			case null:
				return null;
			case TimeInForce.PutInQueue:
			{
				if (type == OrderTypes.Market)
					return null;
					
				if (tillDate == null)
					return "GTC";
				else if (tillDate.Value.IsToday())
					return "DAY";
				else
					return "GTD";
			}
			case TimeInForce.CancelBalance:
				return "IOC";
			case TimeInForce.MatchOrCancel:
				return "FOK";
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static TimeInForce? ToTimeInForce(this string tif)
	{
		if (tif.IsEmpty())
			return null;

		switch (tif)
		{
			case "GTC":
			case "DAY":
			case "GTD":
				return TimeInForce.PutInQueue;
			case "IOC":
				return TimeInForce.CancelBalance;
			case "FOK":
				return TimeInForce.MatchOrCancel;
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderStates? ToOrderState(this string status)
	{
		if (status.IsEmpty())
			return null;

		// new, suspended, partiallyFilled, filled, canceled, expired
		switch (status)
		{
			case "new":
			case "suspended":
			case "partiallyFilled":
				return OrderStates.Active;
			case "filled":
			case "canceled":
			case "expired":
				return OrderStates.Done;
			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}

	public static PairSet<TimeSpan, string> TimeFrames { get; } = new PairSet<TimeSpan, string>
	{
		{ TimeSpan.FromMinutes(1), "M1" },
		{ TimeSpan.FromMinutes(3), "M3" },
		{ TimeSpan.FromMinutes(5), "M5" },
		{ TimeSpan.FromMinutes(15), "M15" },
		{ TimeSpan.FromMinutes(30), "M30" },
		{ TimeSpan.FromHours(1), "H1" },
		{ TimeSpan.FromHours(4), "H4" },
		{ TimeSpan.FromDays(1), "D1" },
		{ TimeSpan.FromDays(3), "D3" },
		{ TimeSpan.FromDays(7), "D7" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
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

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.Remove("/").ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		currency = currency.Insert(currency.Length - (currency.EndsWithIgnoreCase("USDT") ? 4: 3), "/");

		return new SecurityId
		{
			SecurityCode = currency.ToUpperInvariant(),
			BoardCode = BoardCodes.HitBtc,
		};
	}
}