namespace StockSharp.Tradier.Native;

static class Extensions
{
	public static string ToNative<T>(this T value)
		where T : struct, Enum
		=> value.GetAttributeOfType<EnumMemberAttribute>()?.Value ?? value.ToString();

    public static TradierOrderSides ToNative(this Sides side)
        => side switch
        {
            Sides.Buy => TradierOrderSides.Buy,
            Sides.Sell => TradierOrderSides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };

    public static Sides? ToSide(this TradierOrderSides? side)
        => side switch
        {
			null => null,
            TradierOrderSides.Buy or TradierOrderSides.BuyToOpen or TradierOrderSides.BuyToClose or TradierOrderSides.BuyToCover => Sides.Buy,
            TradierOrderSides.Sell or TradierOrderSides.SellToOpen or TradierOrderSides.SellToClose or TradierOrderSides.SellShort => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };

    private static readonly PairSet<TradierSecurityTypes, SecurityTypes> _types = new()
    {
        { TradierSecurityTypes.Stock, SecurityTypes.Stock },
        { TradierSecurityTypes.Etf, SecurityTypes.Fund },
        { TradierSecurityTypes.Index, SecurityTypes.Index },
        { TradierSecurityTypes.Option, SecurityTypes.Option },
    };

    public static TradierSecurityTypes? TryToNative(this SecurityTypes type)
    {
        return _types.TryGetKey(type, out var native) ? native : null;
        //if (_types.TryGetKey(type, out var native))
        //	return native;

        //throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
    }

    public static SecurityTypes ToSecurityType(this TradierSecurityTypes type)
    {
		if (_types.TryGetValue(type, out var st))
            return st;

        throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
    }

    public static TradierOrderClasses ToClass(this SecurityTypes? type)
        => type switch
        {
            SecurityTypes.Option => TradierOrderClasses.Option,
            _ => TradierOrderClasses.Equity,
        };

    public static OptionTypes ToOptionType(this TradierOptionTypes type)
    {
        return type switch
        {
            TradierOptionTypes.Call => OptionTypes.Call,
            TradierOptionTypes.Put => OptionTypes.Put,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
        };
    }

    public static readonly PairSet<TimeSpan, string> TimeFrames = new()
    {
        { TimeSpan.FromTicks(1), "tick" },
        { TimeSpan.FromMinutes(1), "1min" },
        { TimeSpan.FromMinutes(5), "5min" },
        { TimeSpan.FromMinutes(15), "15min" },
        { TimeSpan.FromDays(1), "daily" },
        { TimeSpan.FromDays(7), "weekly" },
        { TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "monthly" },
    };

    public static string ToNative(this TimeSpan timeFrame)
        => TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

    public static TimeSpan ToTimeFrame(this string name)
        => TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

    public static SecurityId ToStockSharp(this string symbol)
        => symbol.IsEmpty() ? default : new ()
        {
            SecurityCode = symbol.ToUpperInvariant(),
            BoardCode = BoardCodes.Tradier,
        };

    public static PortfolioStates? ToPortfolioState(this TradierPortfolioStatuses? status)
    {
        if (status is null)
            return null;

		return status switch
        {
			TradierPortfolioStatuses.Active => PortfolioStates.Active,
			TradierPortfolioStatuses.Closed => PortfolioStates.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
        };
    }

	public static OrderStates? ToOrderState(this TradierOrderStatuses? status)
    {
		if (status is null)
            return null;

		return status switch
        {
			TradierOrderStatuses.Pending or TradierOrderStatuses.PendingCancel => OrderStates.Pending,
			TradierOrderStatuses.Submitted or TradierOrderStatuses.PartiallyFilled or TradierOrderStatuses.Open => OrderStates.Active,
			TradierOrderStatuses.Rejected or TradierOrderStatuses.Error => OrderStates.Failed,
			TradierOrderStatuses.Expired or TradierOrderStatuses.Canceled or TradierOrderStatuses.Filled => OrderStates.Done,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
        };
    }

	public static TradierOrderTypes ToNative(this OrderTypes? type, decimal? stopPrice)
    {
        return type switch
        {
			null or OrderTypes.Limit => TradierOrderTypes.Limit,
			OrderTypes.Market => TradierOrderTypes.Market,
			OrderTypes.Conditional => stopPrice == null ? TradierOrderTypes.Stop : TradierOrderTypes.StopLimit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
        };
    }

	public static OrderTypes? ToOrderType(this TradierOrderTypes? type)
    {
		if (type is null)
            return null;

        return type switch
        {
			TradierOrderTypes.Limit => OrderTypes.Limit,
			TradierOrderTypes.Market => OrderTypes.Market,
			TradierOrderTypes.Stop or TradierOrderTypes.StopLimit => OrderTypes.Conditional,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
        };
    }

	public static TradierOrderDurations ToDuration(this TimeInForce? tif, DateTime? tillDate)
    {
        return tif switch
        {
			null or TimeInForce.PutInQueue => tillDate == null ? TradierOrderDurations.GoodTillCanceled : TradierOrderDurations.GoodTillDate,
            //case TimeInForce.Market:
            //	return "market";
            //case TimeInForce.Conditional:
            //	return stopPrice == null ? "stop" : "stop_limit";
            _ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
        };
    }

	public static TimeInForce? ToTimeInForce(this TradierOrderDurations? duration, out DateTime? tillDate)
    {
        tillDate = null;

		if (duration is null)
            return null;

        // day, gtc, pre, post
        switch (duration)
        {
			case TradierOrderDurations.Day:
                tillDate = Messages.Extensions.Today;
                return TimeInForce.PutInQueue;

			case TradierOrderDurations.GoodTillCanceled:
                return TimeInForce.PutInQueue;

			case TradierOrderDurations.PreMarket:
			case TradierOrderDurations.PostMarket:
			case TradierOrderDurations.GoodTillDate:
                return TimeInForce.PutInQueue;

            default:
                throw new ArgumentOutOfRangeException(nameof(duration), duration, LocalizedStrings.InvalidValue);
        }
    }
}
