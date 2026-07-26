namespace StockSharp.Bithumb;

using System.Globalization;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "bid",
			Sides.Sell => "ask",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side)
	{
		return side?.ToLowerInvariant() switch
		{
			"bid" or "buy" or "up" => Sides.Buy,
			"ask" or "sell" or "dn" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				LocalizedStrings.InvalidValue),
		};
	}

	public static Sides? ToOriginSide(this string side)
		=> side.IsEmpty() ? null : side.ToSide();

	public static OrderStates? ToOrderState(this string state)
	{
		return state?.ToLowerInvariant() switch
		{
			null or "" => null,
			"wait" or "watch" => OrderStates.Active,
			"done" or "cancel" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state,
				LocalizedStrings.InvalidValue),
		};
	}

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result)
			? result
			: null;

	public static string ToSymbol(this SecurityId securityId)
	{
		var parts = securityId.SecurityCode.Split('/');

		return parts.Length == 2
			? $"{parts[1]}-{parts[0]}".ToUpperInvariant()
			: securityId.SecurityCode.ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol)
	{
		symbol.ThrowIfEmpty(nameof(symbol));

		var parts = symbol.Split('-');
		var securityCode = parts.Length == 2
			? $"{parts[1]}/{parts[0]}"
			: symbol;

		return new SecurityId
		{
			SecurityCode = securityCode.ToUpperInvariant(),
			BoardCode = BoardCodes.Bithumb,
		};
	}
}
