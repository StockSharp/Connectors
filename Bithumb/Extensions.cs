namespace StockSharp.Bithumb;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		switch (side)
		{
			case Sides.Buy:
				return "bid";
			case Sides.Sell:
				return "ask";
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static Sides ToSide(this string side)
	{
		switch (side)
		{
			case "bid":
			case "up":
				return Sides.Buy;
			case "ask":
			case "dn":
				return Sides.Sell;
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	private const string _fiatPart = "/KRW";

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode;
	}

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new SecurityId
		{
			SecurityCode = symbol.ToUpperInvariant() + _fiatPart,
			BoardCode = BoardCodes.Bithumb,
		};
	}

	public static DateTime ToDto(this string value)
	{
		var dt = value.ToDateTime(value.Length == 19 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.ffffff");

		return dt.UtcKind();
	}
}