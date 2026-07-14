namespace StockSharp.LATOKEN;

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

	public static string ToNative(this OrderTypes? type)
	{
		return type switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		if (type.IsEmpty())
			return null;

		return type switch
		{
			"limit" => (OrderTypes?)OrderTypes.Limit,
			"market" => (OrderTypes?)OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this TimeInForce? tif)
	{
		return tif switch
		{
			null or TimeInForce.PutInQueue => "GTC",
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTif(this string tif)
	{
		if (tif.IsEmpty())
			return null;

		return tif switch
		{
			"GTC" => (TimeInForce?)TimeInForce.PutInQueue,
			"FOK" => (TimeInForce?)TimeInForce.MatchOrCancel,
			"IOC" => (TimeInForce?)TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	//public static string ToSymbol(this SecurityId securityId)
	//{
	//	return securityId.SecurityCode.Replace('/', '_').ToLowerInvariant();
	//}

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new SecurityId
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Latoken,
		};
	}

	public static OrderStates? ToOrderState(this string status)
	{
		if (status.IsEmpty())
			return null;

		return status switch
		{
			"PLACED" => (OrderStates?)OrderStates.Active,
			"CLOSED" => (OrderStates?)OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}
}