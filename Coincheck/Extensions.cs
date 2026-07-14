namespace StockSharp.Coincheck;

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
		return securityId.SecurityCode.Replace('/', '_').ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Coincheck,
		};
	}

	public static DateTime ToDto(this string value, string format = "yyyy-MM-ddTHH:mm:ss.fffZ")
	{
		return value.ToDateTime(format).ToUniversalTime();
	}
}