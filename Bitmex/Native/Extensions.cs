namespace StockSharp.Bitmex.Native;

static class Extensions
{
    public static bool? ToTickDirection(this string side)
    {
        switch (side)
        {
            case "ZeroMinusTick":
                return false;
            case "PlusTick":
            case "ZeroPlusTick":
                return true;
            //case Sides.Sell:
            //	return "Sell";
            default:
                return null;
                //throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
        }
    }

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
            case "bid":
            case "buy":
                return Sides.Buy;
            case "ask":
            case "sell":
                return Sides.Sell;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
        }
    }

    public static string ToNative(this BitmexOrderExecInstructions? instructions)
    {
        return instructions?.SplitMask().Select(i => i.To<string>()).JoinComma();
    }

    public static BitmexOrderExecInstructions? ToExecInst(this string instructions)
    {
        if (instructions.IsEmpty())
            return null;

        return instructions.SplitByComma().Select(i => i.To<BitmexOrderExecInstructions>()).JoinMask();
    }

    public static string ToNative(this TimeInForce? tif, DateTime? expiryDate)
    {
        switch (tif)
        {
            case null:
            case TimeInForce.PutInQueue:
                {
                    if (expiryDate.IsToday())
                        return "Day";

                    return "GoodTillCancel";
                }
            case TimeInForce.MatchOrCancel:
                return "FillOrKill";
            case TimeInForce.CancelBalance:
                return "ImmediateOrCancel";
            default:
                throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
        }
    }

    public static TimeInForce? ToTimeInForce(this string tif, out DateTime? expiryDate)
    {
        expiryDate = null;

        switch (tif?.ToLowerInvariant())
        {
            case null:
                return null;
            case "day":
                expiryDate = Messages.Extensions.Today;
                return TimeInForce.PutInQueue;
            case "goodtillcancel":
                return TimeInForce.PutInQueue;
            case "fillorkill":
                return TimeInForce.MatchOrCancel;
            case "immediateorcancel":
                return TimeInForce.CancelBalance;
            default:
                throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
        }
    }

    public static string ToNative(this OrderTypes? type, BitmexOrderCondition condition)
    {
        switch (type)
        {
            case null:
            case OrderTypes.Limit:
                return "Limit";
            case OrderTypes.Market:
                return "Market";
            case OrderTypes.Conditional:
                {
                    if (condition == null)
                        throw new ArgumentNullException(nameof(condition));

                    switch (condition.StopType)
                    {
                        case null:
                        case BitmexOrderTypes.Stop:
                            return "Stop";
                        case BitmexOrderTypes.StopLimit:
                            return "StopLimit";
                        case BitmexOrderTypes.MarketIfTouched:
                            return "MarketIfTouched";
                        case BitmexOrderTypes.LimitIfTouched:
                            return "LimitIfTouched";
                        default:
                            throw new ArgumentOutOfRangeException(nameof(condition), condition.StopType, LocalizedStrings.InvalidValue);
                    }
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
        }
    }

    public static OrderTypes? ToOrderType(this string type, out BitmexOrderCondition condition)
    {
        condition = null;

        switch (type?.ToLowerInvariant())
        {
            case null:
            case "":
                return null;
            case "limit":
                return OrderTypes.Limit;
            case "market":
                return OrderTypes.Market;
            case "stop":
                condition = new BitmexOrderCondition { StopType = BitmexOrderTypes.Stop };
                return OrderTypes.Conditional;
            case "stoplimit":
                condition = new BitmexOrderCondition { StopType = BitmexOrderTypes.StopLimit };
                return OrderTypes.Conditional;
            case "marketiftouched":
                condition = new BitmexOrderCondition { StopType = BitmexOrderTypes.MarketIfTouched };
                return OrderTypes.Conditional;
            case "limitiftouched":
                condition = new BitmexOrderCondition { StopType = BitmexOrderTypes.LimitIfTouched };
                return OrderTypes.Conditional;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
        }
    }

    public static readonly PairSet<TimeSpan, string> TimeFrames = new()
    {
        { TimeSpan.FromMinutes(1), "1m" },
        { TimeSpan.FromMinutes(5), "5m" },
        { TimeSpan.FromHours(1), "1h" },
        { TimeSpan.FromDays(1), "1d" },
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

    //public static string ToCurrency(this SecurityId securityId)
    //{
    //	return securityId.SecurityCode.Replace'/', '_').ToLowerInvariant();
    //}

    public static SecurityId ToStockSharp(this string contract)
    {
        return new SecurityId
        {
            SecurityCode = contract.ToUpperInvariant(),
            BoardCode = BoardCodes.Bitmex,
        };
    }

    public static decimal? GetBalance(this Order order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        // LeavesQty = 0 for cancelled orders
        // https://github.com/BitMEX/sample-market-maker/pull/5#issuecomment-293342287
        return order.OrdStatus.EqualsIgnoreCase("Filled") ? 0 : (decimal?)(order.OrderQty - order.CumQty);
    }

    public static OrderStates? ToOrderState(this string status)
    {
        switch (status?.ToLowerInvariant())
        {
            case null:
                return null;

            case "pendingnew":
                return OrderStates.Pending;

            case "new":
            case "partiallyfilled":
            case "doneforday":
            case "pendingcancel":
                return OrderStates.Active;

            case "canceled":
            case "filled":
            case "expired":
            case "stopped":
                return OrderStates.Done;

            case "rejected":
                return OrderStates.Failed;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
        }
    }
}