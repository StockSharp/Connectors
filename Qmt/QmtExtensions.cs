namespace StockSharp.Qmt;

using Native.Model;

internal static class QmtExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string ToQmtSymbol(this SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (code.Contains('.'))
			return code.ToUpperInvariant();

		var market = securityId.BoardCode?.ToUpperInvariant() switch
		{
			BoardCodes.Sse or "SH" => "SH",
			BoardCodes.Szse or "SZ" => "SZ",
			BoardCodes.Bse or "BJ" => "BJ",
			_ => throw new ArgumentOutOfRangeException(nameof(securityId), securityId,
				"QMT securities require an SSE, SZSE, or BSE board code."),
		};
		return $"{code}.{market}";
	}

	public static SecurityId ToSecurityId(this string symbol)
	{
		var parts = symbol.ThrowIfEmpty(nameof(symbol)).Split('.');
		if (parts.Length != 2)
			throw new FormatException($"Invalid QMT symbol '{symbol}'.");

		return new()
		{
			SecurityCode = parts[0],
			BoardCode = parts[1].ToUpperInvariant() switch
			{
				"SH" => BoardCodes.Sse,
				"SZ" => BoardCodes.Szse,
				"BJ" => BoardCodes.Bse,
				var market => market,
			},
		};
	}

	public static SecurityId ToSecurityId(this QmtSecurity security)
		=> security.Symbol.ToSecurityId();

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"stock" => SecurityTypes.Stock,
			"fund" or "etf" or "lof" => SecurityTypes.Fund,
			"bond" => SecurityTypes.Bond,
			"index" => SecurityTypes.Index,
			"future" => SecurityTypes.Future,
			"option" => SecurityTypes.Option,
			_ => null,
		};

	public static string ToQmtPeriod(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) => "1m",
			var value when value == TimeSpan.FromMinutes(3) => "3m",
			var value when value == TimeSpan.FromMinutes(5) => "5m",
			var value when value == TimeSpan.FromMinutes(15) => "15m",
			var value when value == TimeSpan.FromMinutes(30) => "30m",
			var value when value == TimeSpan.FromHours(1) => "1h",
			var value when value == TimeSpan.FromDays(1) => "1d",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported QMT candle time frame."),
		};

	public static DateTime ToUtc(this long unixMilliseconds)
	{
		if (unixMilliseconds <= 0)
			return DateTime.UtcNow;
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(unixMilliseconds);
		}
		catch (ArgumentOutOfRangeException)
		{
			return DateTime.UtcNow;
		}
	}

	public static long ToUnixMilliseconds(this DateTime value)
	{
		var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		return checked((utc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond);
	}

	public static OrderStates ToOrderState(this int status)
		=> status switch
		{
			48 or 49 => OrderStates.Pending,
			50 or 51 or 52 or 55 => OrderStates.Active,
			53 or 54 or 56 => OrderStates.Done,
			57 => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("sell") ? Sides.Sell : Sides.Buy;

	public static OrderTypes ToOrderType(this string value)
		=> value.EqualsIgnoreCase("limit") ? OrderTypes.Limit : OrderTypes.Market;
}
