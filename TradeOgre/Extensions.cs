namespace StockSharp.TradeOgre;

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

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.Replace('/', '-').ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('-', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.TradeOgre,
		};
	}

	public static decimal GetBalance(this Native.Model.Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return (decimal)(order.Quantity - order.Filled.Value);
	}
}