namespace StockSharp.Gopax;

static class Extensions
{
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

	public static string ToNative(this OrderTypes? type)
	{
		switch (type)
		{
			case null:
			case OrderTypes.Limit:
				return "limit";
			case OrderTypes.Market:
				return "market";
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		if (type.IsEmpty())
			return null;

		switch (type)
		{
			case "limit":
				return OrderTypes.Limit;
			case "market":
				return OrderTypes.Market;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode.Replace('/', '-').ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new SecurityId
		{
			SecurityCode = symbol.Replace('-', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Gopax,
		};
	}

	public static int ToNative(this TimeSpan timeFrame)
	{
		return (int)timeFrame.TotalMinutes;
	}

	public static TimeSpan ToTimeFrame(this int native)
	{
		return TimeSpan.FromMinutes(native);
	}

	public static OrderStates? ToOrderState(this string status)
	{
		switch (status)
		{
			case "placed":
				return OrderStates.Active;
			case "cancelled":
				return OrderStates.Done;
			case "completed":
				return OrderStates.Done;
			case "updated":
				return OrderStates.Active;
			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}
}