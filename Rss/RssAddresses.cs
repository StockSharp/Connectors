namespace StockSharp.Rss;

using System.Collections.Generic;
using System.Linq;

using Ecng.ComponentModel;

/// <summary>
/// The most popular RSS feeds.
/// </summary>
public static class RssAddresses
{
	/// <summary>
	/// Reuters (economy).
	/// </summary>
	public const string Reuters = "https://ir.thomsonreuters.com/rss/news-releases.xml";

	///// <summary>
	///// NASDAQ (Commodities).
	///// </summary>
	//public const string Nasdaq = "https://www.nasdaq.com/feed/rssoutbound?category=Commodities";

	/// <summary>
	/// MOEX.
	/// </summary>
	public const string Moex = "https://www.moex.com/export/news.aspx";

	///// <summary>
	///// Trading Economics (Russia).
	///// </summary>
	//public const string TradingEconomics = "https://tradingeconomics.com/russia/rss";

	///// <summary>
	///// Technical Traders.
	///// </summary>
	//public const string TechnicalTraders = "https://www.thetechnicaltraders.com/feed/";

	///// <summary>
	///// DailyFX.
	///// </summary>
	//public const string DailyFX = "https://www.dailyfx.com/feeds/market-news";

	/// <summary>
	/// MarketWatch.
	/// </summary>
	public const string MarketWatch = "http://feeds.marketwatch.com/marketwatch/bulletins?format=xml";

	/// <summary>
	/// Smart-Lab.
	/// </summary>
	public const string SmartLab = "https://smart-lab.ru/allsignals/rss/";

	/// <summary>
	/// All addresses.
	/// </summary>
	public static string[] All { get; } = typeof(RssAddresses).GetFields().Select(f => f.GetValue(null)).Cast<string>().ToArray();
}

class RssAddressesSource : ItemsSourceBase<string>
{
    protected override IEnumerable<string> GetValues() => RssAddresses.All;
}