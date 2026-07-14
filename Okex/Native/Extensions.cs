namespace StockSharp.Okex.Native;

static class Extensions
{
    static class OkexOrderTypes
    {
        public const string Market = "market";
        public const string Limit = "limit";
        public const string PostOnly = "post_only";
        public const string FillOrKill = "fok";
        public const string ImmediateOrCancel = "ioc";
        public const string OptimalFillIoc = "optimal_fill_ioc";
    }

    public static string ToNative(this MarginModes? mode, bool isSpot, bool leading)
        => mode switch
        {
			null => isSpot ? (leading ? "spot_isolated" : "cash") : "isolated",
            MarginModes.Cross => "cross",
            MarginModes.Isolated => "isolated",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, LocalizedStrings.InvalidValue)
        };

    public static MarginModes? ToMarginMode(this string mode)
        => mode?.ToLowerInvariant() switch
        {
            "cross" => MarginModes.Cross,
            "isolated" or "spot_isolated" => MarginModes.Isolated,
            _ => null
        };

    public static string ToNative(this Sides side) =>
        side switch
        {
            Sides.Buy => "buy",
            Sides.Sell => "sell",
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue)
        };

    public static Sides? ToSide(this string side) => side.IsEmptyOrWhiteSpace() ? null : side.To<Sides?>();

    public static TimeInForce? ToTimeInForce(this string orderType, out bool? postOnly)
    {
        postOnly = null;

        switch (orderType)
        {
            case OkexOrderTypes.Limit:
                return null;
            case OkexOrderTypes.PostOnly:
                postOnly = true;
                return null;
            case OkexOrderTypes.FillOrKill:
                return TimeInForce.MatchOrCancel;
            case OkexOrderTypes.ImmediateOrCancel:
            case OkexOrderTypes.OptimalFillIoc:
                return TimeInForce.CancelBalance;
            case OkexOrderTypes.Market:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue);
        }
    }

    public static string GetNativeOrderType(this decimal? price, bool? postOnly, TimeInForce? tif, bool? matchPrice)
    {
        string otype = null;

        if (postOnly == true)
            otype = OkexOrderTypes.PostOnly;
        else if (matchPrice == true)
            otype = OkexOrderTypes.OptimalFillIoc;
        else if (tif != null && tif != TimeInForce.PutInQueue)
        {
            otype = tif is TimeInForce.CancelBalance ? OkexOrderTypes.ImmediateOrCancel :
                    tif is TimeInForce.MatchOrCancel ? OkexOrderTypes.FillOrKill :
                    throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
        }
        else if (price != null)
            otype = OkexOrderTypes.Limit;

        otype ??= OkexOrderTypes.Market;

        return otype;
    }

    public static string ToNativeMatchPrice(this bool matchPrice)
    {
        // Whether order is placed at best counter party price (‘0’:no ‘1’:yes).
        // The parameter is defaulted as ‘0’.
        // If it is set as '1', the price parameter will be ignored.
        // When posting orders at best bid price, order_type can only be '0' (regular order)
        return matchPrice ? "1" : "0";
    }

    public static OrderTypes ToOrderType(this string okexOrdType)
    {
		return okexOrdType switch
		{
			OkexOrderTypes.Limit or OkexOrderTypes.PostOnly or
			OkexOrderTypes.FillOrKill or OkexOrderTypes.ImmediateOrCancel
				=> OrderTypes.Limit,

			OkexOrderTypes.Market or OkexOrderTypes.OptimalFillIoc
				=> OrderTypes.Market,

			_ => throw new ArgumentOutOfRangeException(nameof(okexOrdType), okexOrdType, LocalizedStrings.InvalidValue),
		};
	}

    public static readonly PairSet<TimeSpan, string> TimeFrames = new()
    {
        { TimeSpan.FromMinutes(1),  "1m" },
        { TimeSpan.FromMinutes(3),  "3m" },
        { TimeSpan.FromMinutes(5),  "5m" },
        { TimeSpan.FromMinutes(15), "15m" },
        { TimeSpan.FromMinutes(30), "30m" },
        { TimeSpan.FromHours(1),    "1H" },
        { TimeSpan.FromHours(2),    "2H" },
        { TimeSpan.FromHours(4),    "4H" },
        { TimeSpan.FromHours(6),    "6H" },
        { TimeSpan.FromHours(12),   "12H" },
        { TimeSpan.FromDays(1),     "1D" },
        { TimeSpan.FromDays(7),     "1W" },
    };

    public static string ToNative(this TimeSpan timeFrame, bool throwErr = true)
    {
        var tf = TimeFrames.TryGetValue(timeFrame);
        if (tf == null && throwErr)
            throw new InvalidOperationException($"timeframe not supported '{timeFrame}'");
        return tf;
    }

    public static TimeSpan ToTimeframe(this string native) => TimeFrames.TryGetKey(native, out var tf) ? tf : throw new InvalidOperationException($"unknown native timeframe '{native}'");

    public static string ToNative(this SecurityId securityId)
    {
        return securityId.SecurityCode.Replace('/', '-').ToUpperInvariant();
    }

    public static SecurityId ToStockSharp(this string currency)
    {
        return new SecurityId
        {
            SecurityCode = currency.Replace('-', '/').ToUpperInvariant(),
            BoardCode = BoardCodes.Okex,
        };
    }

    public static decimal GetFilledSize(this OkexOrder order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return order.AccumulatedFilledSize ?? 0;
    }

    public static decimal GetSize(this OkexOrder order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return order.Size ?? 0;
    }

    public static decimal GetBalance(this OkexOrder order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return order.GetSize() - order.GetFilledSize();
    }

    public static OrderStates ToOrderState(this string okexOrderState)
    {
		return (okexOrderState?.ToLowerInvariant()) switch
		{
			"canceled" or "filled" => OrderStates.Done,
			"live" or "partially_filled" => OrderStates.Active,
			_ => throw new ArgumentOutOfRangeException(nameof(okexOrderState), okexOrderState, LocalizedStrings.InvalidValue),
		};
	}

	public const SecurityTypes Margin = (SecurityTypes)(-1);
	public const SecurityTypes Any = (SecurityTypes)(-2);

	private static readonly PairSet<SecurityTypes, string> _secTypeMap = new()
	{
		{ SecurityTypes.CryptoCurrency, "SPOT" },
		{ SecurityTypes.Swap, "SWAP" },
		{ SecurityTypes.Future, "FUTURES" },
		{ SecurityTypes.Option, "OPTION" },
		{ Margin, "MARGIN" },
		{ Any, "ANY" },
	};

	public static SecurityTypes ToSecurityType(this string type)
		=> _secTypeMap[type];

	public static string ToNative(this SecurityTypes type)
		=> _secTypeMap[type];

	public static bool IsHistoryCandlesSupported(this SecurityTypes type)
		=> type switch
		{
			SecurityTypes.CryptoCurrency => true,
			SecurityTypes.Swap => true,
			SecurityTypes.Future => true,
			_ => false
		};

	public static CandleStates ToCandleState(this int confirm)
		=> confirm switch
		{
			0 => CandleStates.Active,
			1 => CandleStates.Finished,
			_ => throw new ArgumentOutOfRangeException(nameof(confirm), confirm, LocalizedStrings.InvalidValue)
		};

	public static bool? ToNative(this OrderPositionEffects effect)
		=> effect switch
		{
			OrderPositionEffects.Default => null,
			OrderPositionEffects.OpenOnly => false,
			OrderPositionEffects.CloseOnly => true,
			_ => throw new ArgumentOutOfRangeException(nameof(effect), effect, LocalizedStrings.InvalidValue)
		};

	public static OrderPositionEffects? ToPositionEffect(this bool reduceOnly)
		=> reduceOnly ? OrderPositionEffects.CloseOnly : OrderPositionEffects.OpenOnly;
}
