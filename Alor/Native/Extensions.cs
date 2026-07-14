namespace StockSharp.Alor.Native;

static class Extensions
{
    public static string ToNative(this Sides side)
    {
        return side switch
        {
            Sides.Buy => "buy",
            Sides.Sell => "sell",
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };
    }

    public static Sides ToSide(this string side)
    {
        return side switch
        {
            "buy" => Sides.Buy,
            "sell" => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };
    }

    public static OrderTypes ToOrderType(this string type)
    {
        return type.ToLowerInvariant() switch
        {
            null or "" or "limit" => OrderTypes.Limit,
            "market" => OrderTypes.Market,
            "stoplimit" or "stop" => OrderTypes.Conditional,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
        };
    }

	public static string ToNative(this TimeInForce? tif, DateTime? expiry)
	{
		return tif switch
		{
			null or TimeInForce.PutInQueue => expiry is null ? "goodtillcancelled" : "oneday",
			TimeInForce.CancelBalance => "immediateorcancel",
			TimeInForce.MatchOrCancel => "fillorkill",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif)
	{
		if (tif.IsEmpty())
			return null;

		return tif.ToLowerInvariant() switch
		{
			"oneday" => TimeInForce.PutInQueue,
			"immediateorcancel" => TimeInForce.CancelBalance,
			"fillorkill" => TimeInForce.MatchOrCancel,
			"goodtillcancelled" => TimeInForce.PutInQueue,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static PairSet<TimeSpan, string> TimeFrames { get; } = new()
    {
        { TimeSpan.FromSeconds(15), "15" },
        { TimeSpan.FromMinutes(1), "60" },
        { TimeSpan.FromHours(1), "3600" },
        { TimeSpan.FromDays(1), "D" },
        { TimeSpan.FromDays(7), "W" },
        { TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "M" },
        { TimeSpan.FromTicks(TimeHelper.TicksPerYear), "Y" },
    };

    public static string ToNative(this TimeSpan timeFrame)
        => TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

    public static TimeSpan ToTimeFrame(this string name)
        => TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

    public static OrderStates ToOrderState(this string status)
    {
        return status switch
        {
            "working" => OrderStates.Active,
            "filled" => OrderStates.Done,
            "canceled" => OrderStates.Done,
            "rejected" => OrderStates.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
        };
    }

    public static SecurityStates ToSecurityState(this int status)
    {
        return status switch
        {
            17 => SecurityStates.Trading,
            _ => SecurityStates.Stoped,
        };
    }

    public static SecurityTypes? ToSecurityType(this string type)
    {
        return type?.ToLowerInvariant() switch
        {
            null or "" => null,
            "cs" => SecurityTypes.Stock,
            "mf" => SecurityTypes.Fund,
            "for" => SecurityTypes.Forward,
            "eusov" => SecurityTypes.Bond,
            "rdr" => SecurityTypes.Adr,
            _ => type.StartsWithIgnoreCase("פü‏קונס") ? SecurityTypes.Future : (type.ContainsIgnoreCase(" put ") || type.ContainsIgnoreCase(" call ") ? SecurityTypes.Option : SecurityTypes.Stock),
        };
    }

	public static string GetCondition(this OrderRegisterMessage regMsg, out AlorOrderCondition condition)
	{
		condition = (AlorOrderCondition)regMsg.Condition ?? throw new InvalidOperationException("Condition is empty.");

		if (regMsg.Side == Sides.Sell)
			return condition.IsTakeProfit ? "More" : "Less";
		else
			return condition.IsTakeProfit ? "Less" : "More";
	}

	public static OptionTypes? ToOptionType(this string type)
	{
		return type?.ToLowerInvariant() switch
		{
			"call" => OptionTypes.Call,
			"put" => OptionTypes.Put,
			_ => null,
		};
	}
}