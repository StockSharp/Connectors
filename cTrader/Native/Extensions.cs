namespace StockSharp.cTrader.Native;

static class Extensions
{
	public static ProtoOAOrderType ToNative(this OrderTypes? orderType, cTraderOrderCondition condition)
	{
		ProtoOAOrderType toNative()
		{
			if (condition is null)
				return ProtoOAOrderType.Limit;

			if (condition.StopLoss is not null && condition.TakeProfit is not null)
				return ProtoOAOrderType.StopLossTakeProfit;
			else if (condition.Price is null)
				return ProtoOAOrderType.Stop;
			else
				return ProtoOAOrderType.StopLimit;
		}

		return orderType switch
		{
			null or OrderTypes.Limit => ProtoOAOrderType.Limit,
			OrderTypes.Market => ProtoOAOrderType.Market,
			OrderTypes.Conditional => toNative(),
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes FromNative(this ProtoOAOrderType type)
		=> type switch
		{
			ProtoOAOrderType.Limit => OrderTypes.Limit,
			ProtoOAOrderType.Market => OrderTypes.Market,
			_ => OrderTypes.Conditional,
		};

	public static ProtoOATimeInForce ToNative(this TimeInForce tif)
		=> tif switch
		{
			TimeInForce.PutInQueue => ProtoOATimeInForce.GoodTillCancel,
			TimeInForce.MatchOrCancel => ProtoOATimeInForce.FillOrKill,
			TimeInForce.CancelBalance => ProtoOATimeInForce.ImmediateOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce FromNative(this ProtoOATimeInForce tif)
		=> tif switch
		{
			ProtoOATimeInForce.GoodTillCancel => TimeInForce.PutInQueue,
			ProtoOATimeInForce.FillOrKill => TimeInForce.MatchOrCancel,
			ProtoOATimeInForce.ImmediateOrCancel => TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};

	public static ProtoOATradeSide ToNative(this Sides side)
		=> side switch
		{
			Sides.Buy => ProtoOATradeSide.Buy,
			Sides.Sell => ProtoOATradeSide.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides FromNative(this ProtoOATradeSide side)
		=> side switch
		{
			ProtoOATradeSide.Buy => Sides.Buy,
			ProtoOATradeSide.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderStates FromNative(this ProtoOAOrderStatus status)
		=> status switch
		{
			ProtoOAOrderStatus.OrderStatusAccepted => OrderStates.Active,

			ProtoOAOrderStatus.OrderStatusFilled or ProtoOAOrderStatus.OrderStatusExpired or
			ProtoOAOrderStatus.OrderStatusCancelled => OrderStates.Done,

			ProtoOAOrderStatus.OrderStatusRejected => OrderStates.Failed,

			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	public static OrderStates? FromNative(this ProtoOADealStatus status)
		=> status switch
		{
			ProtoOADealStatus.Filled => OrderStates.Done,
			ProtoOADealStatus.PartiallyFilled => OrderStates.Active,
			ProtoOADealStatus.Error or ProtoOADealStatus.Rejected or ProtoOADealStatus.InternallyRejected => OrderStates.Failed,
			ProtoOADealStatus.Missed => null,

			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	public static long? TryToAccountId(this string portfolioName)
		=> long.TryParse(portfolioName, out var accId) ? accId : null;

	public static long ToAccountId(this string portfolioName)
		=> portfolioName.To<long>();

	public static string ToPortfolioName(this long accountId)
		=> accountId.To<string>();

	public static string ToPortfolioName(this ulong accountId)
		=> accountId.To<string>();

	public static readonly PairSet<TimeSpan, ProtoOATrendbarPeriod> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), ProtoOATrendbarPeriod.M1 },
		{ TimeSpan.FromMinutes(2), ProtoOATrendbarPeriod.M2 },
		{ TimeSpan.FromMinutes(3), ProtoOATrendbarPeriod.M3 },
		{ TimeSpan.FromMinutes(4), ProtoOATrendbarPeriod.M4 },
		{ TimeSpan.FromMinutes(5), ProtoOATrendbarPeriod.M5 },
		{ TimeSpan.FromMinutes(10), ProtoOATrendbarPeriod.M10 },
		{ TimeSpan.FromMinutes(15), ProtoOATrendbarPeriod.M15 },
		{ TimeSpan.FromMinutes(30), ProtoOATrendbarPeriod.M30 },
		{ TimeSpan.FromHours(1), ProtoOATrendbarPeriod.H1 },
		{ TimeSpan.FromHours(4), ProtoOATrendbarPeriod.H4 },
		{ TimeSpan.FromHours(12), ProtoOATrendbarPeriod.H12 },
		{ TimeSpan.FromDays(1), ProtoOATrendbarPeriod.D1 },
		{ TimeSpan.FromDays(7), ProtoOATrendbarPeriod.W1 },
		{ TimeSpan.FromDays(30), ProtoOATrendbarPeriod.Mn1 },
	};

	public static ProtoOATrendbarPeriod ToNative(this TimeSpan timeFrame)
	{
		if (!TimeFrames.TryGetValue(timeFrame, out var period))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return period;
	}

	public static TimeSpan FromNative(this ProtoOATrendbarPeriod period)
	{
		if (!TimeFrames.TryGetKey(period, out var timeFrame))
			throw new ArgumentOutOfRangeException(nameof(period), period, LocalizedStrings.InvalidValue);

		return timeFrame;
	}

	public static decimal FromMonetary(this long monetary) => monetary / 100.0m;
	public static decimal FromMonetary(this ulong monetary) => monetary / 100.0m;

	public static long ToMonetary(this decimal amount) => (amount * 100).To<long>();

	public static long GetPosSize(this ProtoOAPosition pos)
	{
		if (pos is null)
			throw new ArgumentNullException(nameof(pos));

		return (pos.TradeData.TradeSide == ProtoOATradeSide.Buy ? 1 : -1) * pos.TradeData.Volume;
	}
}