namespace StockSharp.RakutenRss;

static class RakutenRssExtensions
{
	public static string ToNativeCode(this SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (code.Contains('.'))
			return code;
		return securityId.BoardCode?.ToUpperInvariant() switch
		{
			"JAX" => code + ".JAX",
			"JNX" => code + ".JNX",
			"TSE" or "XTKS" or "JPX" or null or "" => code + ".T",
			_ when securityId.IsDerivative() => code,
			_ => code + ".T",
		};
	}

	public static bool IsDerivative(this SecurityId securityId)
		=> securityId.BoardCode?.Contains("OSE", StringComparison.OrdinalIgnoreCase) == true ||
			securityId.BoardCode?.Contains("FOP", StringComparison.OrdinalIgnoreCase) == true ||
			securityId.SecurityCode?.Length == 9;

	public static SecurityId ToSecurityId(this string code, string market, bool derivative)
		=> new()
		{
			SecurityCode = code?.Split('.')[0],
			BoardCode = derivative ? "OSE" : market switch
			{
				"JAX" => "JAX",
				"JNX" => "JNX",
				_ => BoardCodes.Tse,
			},
		};

	public static Sides ToSide(this string value)
		=> value?.Contains('売') == true ? Sides.Sell : Sides.Buy;

	public static OrderStates ToOrderState(this string status)
	{
		if (status.IsEmpty())
			return OrderStates.Pending;
		if (status.Contains("約定", StringComparison.Ordinal))
			return OrderStates.Done;
		if (status.Contains("取消済", StringComparison.Ordinal) ||
			status.Contains("訂正済", StringComparison.Ordinal))
			return OrderStates.Done;
		if (status.Contains("出来ず", StringComparison.Ordinal))
			return OrderStates.Failed;
		return OrderStates.Active;
	}

	public static string ToNativeTimeFrame(this TimeSpan value)
		=> value switch
		{
			{ TotalMinutes: 1 } => "1M",
			{ TotalMinutes: 2 } => "2M",
			{ TotalMinutes: 3 } => "3M",
			{ TotalMinutes: 4 } => "4M",
			{ TotalMinutes: 5 } => "5M",
			{ TotalMinutes: 10 } => "10M",
			{ TotalMinutes: 15 } => "15M",
			{ TotalMinutes: 30 } => "30M",
			{ TotalHours: 1 } => "60M",
			{ TotalHours: 2 } => "2H",
			{ TotalHours: 4 } => "4H",
			{ TotalHours: 8 } => "8H",
			{ TotalDays: 1 } => "D",
			{ TotalDays: 7 } => "W",
			{ TotalDays: >= 28 and <= 31 } => "M",
			_ => throw new NotSupportedException(
				$"MARKETSPEED II RSS does not support {value} candles."),
		};
}
