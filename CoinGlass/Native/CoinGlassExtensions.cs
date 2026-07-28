namespace StockSharp.CoinGlass.Native;

static class CoinGlassExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(8)] = "8h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
		};

	public static IEnumerable<TimeSpan> TimeFrames
		=> _timeFrames.Keys;

	public static string ToInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new NotSupportedException(
				$"CoinGlass does not support the {timeFrame} interval.");

	public static string ToApiName(this CoinGlassMarketTypes marketType)
		=> marketType switch
		{
			CoinGlassMarketTypes.Futures => "futures",
			CoinGlassMarketTypes.Spot => "spot",
			CoinGlassMarketTypes.Options => "option",
			CoinGlassMarketTypes.BitcoinEtf => "bitcoin",
			CoinGlassMarketTypes.EthereumEtf => "ethereum",
			_ => throw new ArgumentOutOfRangeException(
				nameof(marketType), marketType, null),
		};
}
