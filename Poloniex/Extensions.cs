namespace StockSharp.Poloniex;

static class Extensions
{
	public static string ToNative(this Sides value)
	{
		return value switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string value)
	{
		return (value?.ToLowerInvariant()) switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this int value)
	{
		return value switch
		{
			0 => Sides.Sell,
			1 => Sides.Buy,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode?.Replace('/', '_').ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		//if (currency.Length > 3 && currency[3] != '/')
		//	currency = currency.Insert(3, "/");

		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Poloniex,
		};
	}

	public static DateTime ToDto(this string value, string format = "yyyy-MM-dd HH:mm:ss")
	{
		return value.ToDateTime(format).UtcKind();
	}
}