namespace StockSharp.Xtp;

internal static class XtpExtensions
{
	private static readonly TimeSpan _chinaOffset = TimeSpan.FromHours(8);

	public static XtpExchange ToXtpExchange(this SecurityId securityId)
		=> securityId.BoardCode?.ToUpperInvariant() switch
		{
			BoardCodes.Sse => XtpExchange.Shanghai,
			BoardCodes.Szse => XtpExchange.Shenzhen,
			BoardCodes.Bse => XtpExchange.Beijing,
			_ => throw new ArgumentOutOfRangeException(nameof(securityId), securityId, "XTP supports SSE, SZSE and BSE boards."),
		};

	public static XtpMarket ToXtpMarket(this SecurityId securityId)
		=> securityId.BoardCode?.ToUpperInvariant() switch
		{
			BoardCodes.Sse => XtpMarket.Shanghai,
			BoardCodes.Szse => XtpMarket.Shenzhen,
			BoardCodes.Bse => XtpMarket.Beijing,
			_ => throw new ArgumentOutOfRangeException(nameof(securityId), securityId, "XTP supports SSE, SZSE and BSE boards."),
		};

	public static SecurityId ToSecurityId(this string ticker, int marketOrExchange)
		=> new()
		{
			SecurityCode = ticker,
			BoardCode = marketOrExchange switch
			{
				1 => BoardCodes.Szse,
				2 => BoardCodes.Sse,
				3 => BoardCodes.Bse,
				_ => BoardCodes.Sse,
			},
		};

	public static SecurityId ToQuoteSecurityId(this string ticker, int exchange)
		=> new()
		{
			SecurityCode = ticker,
			BoardCode = exchange switch
			{
				(int)XtpExchange.Shanghai => BoardCodes.Sse,
				(int)XtpExchange.Shenzhen => BoardCodes.Szse,
				(int)XtpExchange.Beijing => BoardCodes.Bse,
				_ => BoardCodes.Sse,
			},
		};

	public static SecurityTypes? ToSecurityType(this int type)
		=> type switch
		{
			0 or 1 or 2 or 4 => SecurityTypes.Stock,
			3 => SecurityTypes.Index,
			5 or 6 or 7 or 8 or 12 => SecurityTypes.Bond,
			14 or 15 or 16 or 17 or 18 or 19 or 22 or 23 or 24 or 26 => SecurityTypes.Fund,
			29 or 30 => SecurityTypes.Option,
			_ => null,
		};

	public static DateTime ToXtpTime(this long value)
	{
		if (value <= 0)
			return DateTime.UtcNow;

		var text = value.ToString("D17", CultureInfo.InvariantCulture);
		return DateTime.TryParseExact(text, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
			? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _chinaOffset).UtcDateTime
			: DateTime.UtcNow;
	}

	public static OrderStates ToOrderState(this int status)
		=> status switch
		{
			0 => OrderStates.Pending,
			1 or 3 or 5 => OrderStates.Done,
			2 or 4 => OrderStates.Active,
			6 or 7 => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static Sides ToSide(this int side)
		=> side is 2 or 8 or 22 or 23 or 26 or 29 ? Sides.Sell : Sides.Buy;

	public static Exception ToException(this XtpNativeError error, string operation)
		=> new InvalidOperationException($"XTP {operation} error {error.Id}: {error.Message}");
}
