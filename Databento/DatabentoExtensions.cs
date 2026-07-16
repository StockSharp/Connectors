namespace StockSharp.Databento;

using StockSharp.Databento.Native;

internal static class DatabentoExtensions
{
	private const decimal _priceScale = 1_000_000_000m;

	public static string ToApi(this DatabentoSymbologyTypes value)
		=> value switch
		{
			DatabentoSymbologyTypes.RawSymbol => "raw_symbol",
			DatabentoSymbologyTypes.InstrumentId => "instrument_id",
			DatabentoSymbologyTypes.Parent => "parent",
			DatabentoSymbologyTypes.Continuous => "continuous",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static DateTime ToUtc(this ulong nanoseconds)
	{
		if (nanoseconds == ulong.MaxValue)
			return default;
		var ticks = nanoseconds / 100;
		if (ticks > (ulong)(DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks))
			return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
		return DateTime.UnixEpoch.AddTicks((long)ticks);
	}

	public static long ToUnixNanoseconds(this DateTime time)
	{
		var utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
		return checked((utc.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
	}

	public static decimal? ToPrice(this long value)
		=> value == long.MaxValue ? null : value / _priceScale;

	public static Sides? ToSide(this byte value)
		=> value switch
		{
			(byte)'B' => Sides.Buy,
			(byte)'A' => Sides.Sell,
			_ => null,
		};

	public static SecurityTypes? ToSecurityType(this byte instrumentClass)
		=> instrumentClass switch
		{
			(byte)'B' => SecurityTypes.Bond,
			(byte)'C' or (byte)'P' or (byte)'T' => SecurityTypes.Option,
			(byte)'F' or (byte)'S' => SecurityTypes.Future,
			(byte)'I' => SecurityTypes.Index,
			(byte)'K' => SecurityTypes.Stock,
			(byte)'X' => SecurityTypes.Currency,
			(byte)'Y' => SecurityTypes.Commodity,
			_ => null,
		};

	public static OptionTypes? ToOptionType(this byte instrumentClass)
		=> instrumentClass switch
		{
			(byte)'C' => OptionTypes.Call,
			(byte)'P' => OptionTypes.Put,
			_ => null,
		};

	public static SecurityStates? ToSecurityState(this DbnStatusActions action)
		=> action switch
		{
			DbnStatusActions.Trading => SecurityStates.Trading,
			DbnStatusActions.Halt or DbnStatusActions.Pause or DbnStatusActions.Suspend or
				DbnStatusActions.Close or DbnStatusActions.NotAvailableForTrading => SecurityStates.Stoped,
			_ => null,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var result) ? result : null;

	public static TimeSpan ToTimeFrame(this DbnRecordTypes type)
		=> type switch
		{
			DbnRecordTypes.Ohlcv1Second => TimeSpan.FromSeconds(1),
			DbnRecordTypes.Ohlcv1Minute => TimeSpan.FromMinutes(1),
			DbnRecordTypes.Ohlcv1Hour => TimeSpan.FromHours(1),
			DbnRecordTypes.Ohlcv1Day or DbnRecordTypes.OhlcvEndOfDay => TimeSpan.FromDays(1),
			_ => default,
		};

	public static string ToCandleSchema(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromSeconds(1) => "ohlcv-1s",
			var value when value == TimeSpan.FromMinutes(1) => "ohlcv-1m",
			var value when value == TimeSpan.FromHours(1) => "ohlcv-1h",
			var value when value == TimeSpan.FromDays(1) => "ohlcv-1d",
			_ => throw new NotSupportedException($"Databento does not provide {timeFrame} time-frame candles."),
		};
}
