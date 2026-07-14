namespace StockSharp.Zaif;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "bid",
			Sides.Sell => "ask",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides? ToSide(this string side)
	{
		if (side.IsEmpty())
			return null;

		return side switch
		{
			"bid" or "buy" => (Sides?)Sides.Buy,
			"ask" or "sell" => (Sides?)Sides.Sell,
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
			BoardCode = BoardCodes.Zaif,
		};
	}

	public static DateTime ToDto(this string time)
	{
		return time.ToDateTime("yyyy-MM-dd HH:mm:ss.ffffff").UtcKind();
	}
}