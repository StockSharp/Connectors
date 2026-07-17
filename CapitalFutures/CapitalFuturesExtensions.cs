namespace StockSharp.CapitalFutures;

static class CapitalFuturesExtensions
{
	private static readonly TimeZoneInfo _taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");

	public static SecurityId ToSecurityId(this CapitalInstrumentInfo instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = "TAIFEX",
		};

	public static SecurityTypes ToSecurityType(this short marketNo)
		=> marketNo switch
		{
			2 => SecurityTypes.Future,
			3 => SecurityTypes.Option,
			_ => throw new NotSupportedException($"Capital market {marketNo} is outside domestic futures/options scope."),
		};

	public static short ToMarketNo(this SecurityTypes securityType)
		=> securityType switch
		{
			SecurityTypes.Future => 2,
			SecurityTypes.Option => 3,
			_ => throw new NotSupportedException($"Capital Futures does not support {securityType} through this adapter."),
		};

	public static short ToTradeType(this TimeInForce timeInForce)
		=> timeInForce switch
		{
			TimeInForce.PutInQueue => 0,
			TimeInForce.MatchOrCancel => 1,
			TimeInForce.CancelBalance => 2,
			_ => throw new NotSupportedException($"Capital Futures does not support {timeInForce}."),
		};

	public static TimeInForce ToTimeInForce(this char value)
		=> value switch
		{
			'I' => TimeInForce.MatchOrCancel,
			'F' => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static DateTime FromTaipeiTime(this DateTime value)
		=> TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), _taipeiTimeZone);

	public static CurrencyTypes? ToCurrency(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"TWD" or "NTD" or "NT" => CurrencyTypes.TWD,
			"RMB" or "CNY" => CurrencyTypes.CNY,
			"USD" => CurrencyTypes.USD,
			_ => null,
		};
}
