namespace StockSharp.Exmo;

static class Extensions
{
	public static string ToNative(this Sides side, bool isMarket)
	{
		string native;

		switch (side)
		{
			case Sides.Buy:
				native = "buy";
				break;
			case Sides.Sell:
				native = "sell";
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}

		if (isMarket)
			native = "market_" + native;

		return native;
	}

	public static Sides ToSide(this string side)
	{
		switch (side)
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

	public static OrderTypes ToOrderType(this string type, out Sides side)
	{
		switch (type)
		{
			case "buy":
				side = Sides.Buy;
				return OrderTypes.Limit;
			case "sell":
				side = Sides.Sell;
				return OrderTypes.Limit;
			case "market_buy":
				side = Sides.Buy;
				return OrderTypes.Market;
			case "market_sell":
				side = Sides.Sell;
				return OrderTypes.Market;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.Replace('/', '_').ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Exmo,
		};
	}
}