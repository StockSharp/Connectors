namespace StockSharp.CoinApi;

readonly record struct CoinApiStreamKey(
	CoinApiSocketDataTypes DataType,
	string SymbolId,
	CoinApiPeriodIds PeriodId);

static class CoinApiExtensions
{
	private static readonly Dictionary<TimeSpan, CoinApiPeriodIds> _periods =
		new()
		{
			[TimeSpan.FromSeconds(1)] = CoinApiPeriodIds.Second1,
			[TimeSpan.FromSeconds(2)] = CoinApiPeriodIds.Second2,
			[TimeSpan.FromSeconds(3)] = CoinApiPeriodIds.Second3,
			[TimeSpan.FromSeconds(4)] = CoinApiPeriodIds.Second4,
			[TimeSpan.FromSeconds(5)] = CoinApiPeriodIds.Second5,
			[TimeSpan.FromSeconds(6)] = CoinApiPeriodIds.Second6,
			[TimeSpan.FromSeconds(10)] = CoinApiPeriodIds.Second10,
			[TimeSpan.FromSeconds(15)] = CoinApiPeriodIds.Second15,
			[TimeSpan.FromSeconds(20)] = CoinApiPeriodIds.Second20,
			[TimeSpan.FromSeconds(30)] = CoinApiPeriodIds.Second30,
			[TimeSpan.FromMinutes(1)] = CoinApiPeriodIds.Minute1,
			[TimeSpan.FromMinutes(2)] = CoinApiPeriodIds.Minute2,
			[TimeSpan.FromMinutes(3)] = CoinApiPeriodIds.Minute3,
			[TimeSpan.FromMinutes(4)] = CoinApiPeriodIds.Minute4,
			[TimeSpan.FromMinutes(5)] = CoinApiPeriodIds.Minute5,
			[TimeSpan.FromMinutes(6)] = CoinApiPeriodIds.Minute6,
			[TimeSpan.FromMinutes(10)] = CoinApiPeriodIds.Minute10,
			[TimeSpan.FromMinutes(15)] = CoinApiPeriodIds.Minute15,
			[TimeSpan.FromMinutes(20)] = CoinApiPeriodIds.Minute20,
			[TimeSpan.FromMinutes(30)] = CoinApiPeriodIds.Minute30,
			[TimeSpan.FromHours(1)] = CoinApiPeriodIds.Hour1,
			[TimeSpan.FromHours(2)] = CoinApiPeriodIds.Hour2,
			[TimeSpan.FromHours(3)] = CoinApiPeriodIds.Hour3,
			[TimeSpan.FromHours(4)] = CoinApiPeriodIds.Hour4,
			[TimeSpan.FromHours(6)] = CoinApiPeriodIds.Hour6,
			[TimeSpan.FromHours(8)] = CoinApiPeriodIds.Hour8,
			[TimeSpan.FromHours(12)] = CoinApiPeriodIds.Hour12,
			[TimeSpan.FromDays(1)] = CoinApiPeriodIds.Day1,
			[TimeSpan.FromDays(2)] = CoinApiPeriodIds.Day2,
			[TimeSpan.FromDays(3)] = CoinApiPeriodIds.Day3,
			[TimeSpan.FromDays(5)] = CoinApiPeriodIds.Day5,
			[TimeSpan.FromDays(7)] = CoinApiPeriodIds.Day7,
			[TimeSpan.FromDays(10)] = CoinApiPeriodIds.Day10,
		};

	public static readonly TimeSpan[] TimeFrames =
		[.. _periods.Keys.OrderBy(static value => value)];

	public static CoinApiPeriodIds ToPeriodId(this TimeSpan value)
		=> _periods.TryGetValue(value, out var period)
			? period
			: throw new NotSupportedException(
				$"CoinAPI does not support the {value} candle interval.");

	public static TimeSpan ToTimeFrame(this CoinApiPeriodIds value)
	{
		foreach (var pair in _periods)
			if (pair.Value == value)
				return pair.Key;
		throw new InvalidDataException(
			$"CoinAPI period '{value}' is not a fixed interval.");
	}

	public static SecurityTypes ToSecurityType(this CoinApiSymbolTypes value)
		=> value switch
		{
			CoinApiSymbolTypes.Spot => SecurityTypes.CryptoCurrency,
			CoinApiSymbolTypes.Futures or CoinApiSymbolTypes.Perpetual or
				CoinApiSymbolTypes.DeployerPerpetual => SecurityTypes.Future,
			CoinApiSymbolTypes.Option => SecurityTypes.Option,
			CoinApiSymbolTypes.Index => SecurityTypes.Index,
			CoinApiSymbolTypes.Credit => SecurityTypes.Loan,
			CoinApiSymbolTypes.Contract => SecurityTypes.Swap,
			CoinApiSymbolTypes.OptionCombo or
				CoinApiSymbolTypes.FutureCombo => SecurityTypes.MultiLeg,
			_ => throw new NotSupportedException(
				$"CoinAPI symbol type '{value}' is not supported."),
		};

	public static Sides? ToSide(this CoinApiTakerSides value)
		=> value switch
		{
			CoinApiTakerSides.Buy or CoinApiTakerSides.BuyEstimated => Sides.Buy,
			CoinApiTakerSides.Sell or CoinApiTakerSides.SellEstimated => Sides.Sell,
			_ => null,
		};

	public static CoinApiSocketDataTypes ToBookDataType(int depth)
		=> depth <= 5
			? CoinApiSocketDataTypes.Book5
			: depth <= 20
				? CoinApiSocketDataTypes.Book20
				: CoinApiSocketDataTypes.Book50;

	public static DateTime ParseCoinApiTime(this string value, string field)
	{
		try
		{
			return value.FromIso8601().EnsureUtc();
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new InvalidDataException(
				$"Invalid CoinAPI {field} timestamp '{value}'.", error);
		}
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string FormatCoinApiTime(this DateTime value)
		=> value.EnsureUtc().ToString("O", CultureInfo.InvariantCulture);

	public static string NormalizeFilter(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (value.Length > 256 || value.Any(char.IsControl))
			throw new ArgumentException(
				"CoinAPI filter must contain at most 256 printable characters.",
				nameof(value));
		return value.ToUpperInvariant();
	}
}
