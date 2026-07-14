namespace StockSharp.Yobit;

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
			"bid" or "buy" => Sides.Buy,
			"ask" or "sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.Replace('/', '_').ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Yobit,
		};
	}

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return order.StartAmount - order.Amount;
	}

	public static OrderStates ToOrderState(this int status)
	{
		switch (status)
		{
			case 0: // active
				return OrderStates.Active;

			case 1: // fulfilled and closed
				return OrderStates.Done;

			case 2: // cancelled
			case 3: // cancelled after partially fulfilled
				return OrderStates.Done;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}
}