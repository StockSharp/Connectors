namespace StockSharp.IQFeed;

/// <summary>
/// The list of all available <see cref="IQFeedLevel1Column"/>.
/// </summary>
public class IQFeedLevel1ColumnRegistry
{
	private static readonly Lazy<IQFeedLevel1ColumnRegistry> _instance = new(() => new IQFeedLevel1ColumnRegistry());

	/// <summary>
	/// The <see cref="IQFeedLevel1ColumnRegistry"/> instance.
	/// </summary>
	public static IQFeedLevel1ColumnRegistry Instance => _instance.Value;

	private IQFeedLevel1ColumnRegistry()
	{
		foreach (var field in typeof(IQFeedLevel1ColumnRegistry).GetFields())
		{
			if (field.GetValue(this) is IQFeedLevel1Column column)
				_columns.Add(column.Name, column);
		}
	}

	private readonly SynchronizedDictionary<string, IQFeedLevel1Column> _columns = new(StringComparer.InvariantCultureIgnoreCase);

	private const string _dateFormat = "MM/dd/yyyy";
	private const string _timeFormat = "hh\\:mm\\:ss\\.fff";
	private const string _timeFormatMcs = "hh\\:mm\\:ss\\.ffffff";

	/// <summary>
	/// To get the column by name <see cref="IQFeedLevel1Column.Name"/>.
	/// </summary>
	/// <param name="name">Column name.</param>
	/// <returns>Found column. If the column does not exist then <see langword="null" /> is returned.</returns>
	public IQFeedLevel1Column this[string name] => _columns.TryGetValue(name);

	/// <summary>
	/// All <see cref="IQFeedLevel1Column"/> columns.
	/// </summary>
	public IEnumerable<IQFeedLevel1Column> AllColumns => _columns.Values;

	/// <summary>
	/// Optional <see cref="IQFeedLevel1Column"/> columns.
	/// </summary>
	public IEnumerable<IQFeedLevel1Column> OptionalColumns => _columns.Values.Where(c => !c.IsMandatory);

	/// <summary>
	/// Security code.
	/// </summary>
	public readonly IQFeedLevel1Column Symbol = new("Symbol", typeof(string)) { IsMandatory = true };

	/// <summary>
	/// Exchange id.
	/// </summary>
	public readonly IQFeedLevel1Column ExchangeId = new("Exchange ID", typeof(string)) { IsMandatory = true };

	/// <summary>
	/// Content codes.
	/// </summary>
	public readonly IQFeedLevel1Column MessageContents = new("Message Contents", typeof(string)) { IsMandatory = true };

	/// <summary>
	/// Last trade price.
	/// </summary>
	public readonly IQFeedLevel1Column LastTradePrice = new("Last", typeof(decimal)) { Field = Level1Fields.LastTradePrice };

	/// <summary>
	/// Total session volume.
	/// </summary>
	public readonly IQFeedLevel1Column TotalVolume = new("Total Volume", typeof(decimal)) { Field = Level1Fields.Volume };

	/// <summary>
	/// Highest session price.
	/// </summary>
	public readonly IQFeedLevel1Column High = new("High", typeof(decimal)) { Field = Level1Fields.HighPrice };

	/// <summary>
	/// Lowest session price.
	/// </summary>
	public readonly IQFeedLevel1Column Low = new("Low", typeof(decimal)) { Field = Level1Fields.LowPrice };

	/// <summary>
	/// Bid price.
	/// </summary>
	public readonly IQFeedLevel1Column BidPrice = new("Bid", typeof(decimal)) { Field = Level1Fields.BestBidPrice };

	/// <summary>
	/// Bid change.
	/// </summary>
	public readonly IQFeedLevel1Column BidChange = new("Bid Change", typeof(decimal));

	/// <summary>
	/// Ask price.
	/// </summary>
	public readonly IQFeedLevel1Column AskPrice = new("Ask", typeof(decimal)) { Field = Level1Fields.BestAskPrice };

	/// <summary>
	/// Ask change.
	/// </summary>
	public readonly IQFeedLevel1Column AskChange = new("Ask Change", typeof(decimal));

	/// <summary>
	/// Bid volume.
	/// </summary>
	public readonly IQFeedLevel1Column BidVolume = new("Bid Size", typeof(decimal)) { Field = Level1Fields.BestBidVolume };

	/// <summary>
	/// Ask volume.
	/// </summary>
	public readonly IQFeedLevel1Column AskVolume = new("Ask Size", typeof(decimal)) { Field = Level1Fields.BestAskVolume };

	/// <summary>
	/// Open interest.
	/// </summary>
	public readonly IQFeedLevel1Column OpenInterest = new("Open Interest", typeof(decimal)) { Field = Level1Fields.OpenInterest };

	/// <summary>
	/// Change.
	/// </summary>
	public readonly IQFeedLevel1Column Change = new("Change", typeof(decimal));

	/// <summary>
	/// Percent Change.
	/// </summary>
	public readonly IQFeedLevel1Column PercentChange = new("Percent Change", typeof(decimal)) { Field = Level1Fields.Change };

	/// <summary>
	/// Change from open.
	/// </summary>
	public readonly IQFeedLevel1Column ChangeFromOpen = new("Change From Open", typeof(decimal));

	/// <summary>
	/// Opening price.
	/// </summary>
	public readonly IQFeedLevel1Column Open = new("Open", typeof(decimal)) { Field = Level1Fields.OpenPrice };

	/// <summary>
	/// Closing price.
	/// </summary>
	public readonly IQFeedLevel1Column Close = new("Close", typeof(decimal)) { Field = Level1Fields.ClosePrice };

	/// <summary>
	/// Estimated value.
	/// </summary>
	public readonly IQFeedLevel1Column Settle = new("Settle", typeof(decimal)) { Field = Level1Fields.SettlementPrice };

	/// <summary>
	/// The data delay time in minutes (if not real-time data used).
	/// </summary>
	public readonly IQFeedLevel1Column Delay = new("Delay", typeof(int));

	/// <summary>
	/// The flag which means the short sales allow ability.
	/// </summary>
	public readonly IQFeedLevel1Column ShortSaleRestrictedCode = new("Restricted Code", typeof(string));

	/// <summary>
	/// The value of net yield for mutual funds.
	/// </summary>
	public readonly IQFeedLevel1Column NetAssetValueMutualFonds = new("Net Asset Value", typeof(decimal));

	/// <summary>
	/// The average time to delivery.
	/// </summary>
	public readonly IQFeedLevel1Column AverageDaysMaturity = new("Average Maturity", typeof(decimal));

	/// <summary>
	/// 7 day yield.
	/// </summary>
	public readonly IQFeedLevel1Column SevenDayYield = new("7 Day Yield", typeof(decimal));

	/// <summary>
	/// The market opening event flag.
	/// </summary>
	public readonly IQFeedLevel1Column MarketOpen = new("Market Open", typeof(int));

	/// <summary>
	/// The format of the fractional price.
	/// </summary>
	public readonly IQFeedLevel1Column FractionDisplayCode = new("Fraction Display Code", typeof(string));

	/// <summary>
	/// The precision after the decimal point.
	/// </summary>
	public readonly IQFeedLevel1Column DecimalPrecision = new("Decimal Precision", typeof(string));

	/// <summary>
	/// The volume of the previous trading session.
	/// </summary>
	public readonly IQFeedLevel1Column PrevDayVolume = new("Previous Day Volume", typeof(decimal));

	/// <summary>
	/// Opening range.
	/// </summary>
	public readonly IQFeedLevel1Column OpenRange1 = new("Open Range 1", typeof(decimal));

	/// <summary>
	/// Closing range.
	/// </summary>
	public readonly IQFeedLevel1Column CloseRange1 = new("Close Range 1", typeof(decimal));

	/// <summary>
	/// Opening range.
	/// </summary>
	public readonly IQFeedLevel1Column OpenRange2 = new("Open Range 2", typeof(decimal));

	/// <summary>
	/// Closing range.
	/// </summary>
	public readonly IQFeedLevel1Column CloseRange2 = new("Close Range 2", typeof(decimal));

	/// <summary>
	/// The number of trades per session.
	/// </summary>
	public readonly IQFeedLevel1Column TradeCount = new("Number of Trades Today", typeof(int)) { Field = Level1Fields.TradesCount };

	/// <summary>
	/// VWAP.
	/// </summary>
	public readonly IQFeedLevel1Column VWAP = new("VWAP", typeof(decimal)) { Field = Level1Fields.VWAP };

	/// <summary>
	/// Last trade ID.
	/// </summary>
	public readonly IQFeedLevel1Column LastTradeId = new("TickID", typeof(long)) { Field = Level1Fields.LastTradeId };

	/// <summary>
	/// Indicator code.
	/// </summary>
	public readonly IQFeedLevel1Column FinancialStatusIndicator = new("Financial Status Indicator", typeof(string));

	/// <summary>
	/// Settlement date.
	/// </summary>
	public readonly IQFeedLevel1Column SettlementDate = new("Settlement Date", typeof(DateTime), _dateFormat);

	/// <summary>
	/// Days to expiration.
	/// </summary>
	public readonly IQFeedLevel1Column DaysToExpiration = new("Days to Expiration", typeof(int));

	/// <summary>
	/// The bid market identifier.
	/// </summary>
	public readonly IQFeedLevel1Column BidMarket = new("Bid Market Center", typeof(int));

	/// <summary>
	/// The offer market identifier.
	/// </summary>
	public readonly IQFeedLevel1Column AskMarket = new("Ask Market Center", typeof(int));

	/// <summary>
	/// Possible regions.
	/// </summary>
	public readonly IQFeedLevel1Column AvailableRegions = new("Available Regions", typeof(string));

	/// <summary>
	/// Last trade volume.
	/// </summary>
	public readonly IQFeedLevel1Column LastTradeVolume = new("Last Size", typeof(decimal)) { Field = Level1Fields.LastTradeVolume };

	/// <summary>
	/// Time of last trade.
	/// </summary>
	public readonly IQFeedLevel1Column LastTradeTime = new("Last Time", typeof(TimeSpan), _timeFormatMcs) { Field = Level1Fields.LastTradeTime };

	/// <summary>
	/// The last trade market identifier.
	/// </summary>
	public readonly IQFeedLevel1Column LastTradeMarket = new("Last Market Center", typeof(int));

	/// <summary>
	/// The most frequent trade price.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradePrice = new("Most Recent Trade", typeof(decimal));

	/// <summary>
	/// The most frequent trade volume.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeVolume = new("Most Recent Trade Size", typeof(decimal));

	/// <summary>
	/// Percent Off Average Volume.
	/// </summary>
	public readonly IQFeedLevel1Column PercentOffAverageVolume = new("Percent Off Average Volume", typeof(decimal));

	/// <summary>
	/// The most frequent trade time.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeTime = new("Most Recent Trade Time", typeof(TimeSpan), _timeFormatMcs);

	/// <summary>
	/// The most frequent trade condition.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeConditions = new("Most Recent Trade Conditions", typeof(string));

	/// <summary>
	/// The market identifier of the most frequent trade.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeMarket = new("Most Recent Trade Market Center", typeof(int));

	/// <summary>
	/// The price of the last extended trade.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradePrice = new("Extended Trade", typeof(decimal));

	/// <summary>
	/// The volume of the last extended trade.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradeVolume = new("Extended Trade Size", typeof(decimal));

	/// <summary>
	/// The time of the last extended trade.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradeTime = new("Extended Trade Time", typeof(TimeSpan), _timeFormatMcs);

	/// <summary>
	/// The market identifier of the last extended trade.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradeMarket = new("Extended Trade Market Center", typeof(int));

	/// <summary>
	/// Extended Trading Change.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradingChange = new("Extended Trading Change", typeof(decimal));

	/// <summary>
	/// Extended Trading Difference.
	/// </summary>
	public readonly IQFeedLevel1Column ExtendedTradingDifference = new("Extended Trading Difference", typeof(decimal));

	/// <summary>
	/// Ask time.
	/// </summary>
	public readonly IQFeedLevel1Column AskTime = new("Ask Time", typeof(TimeSpan), _timeFormatMcs) { Field = Level1Fields.BestAskTime };

	/// <summary>
	/// Bid time.
	/// </summary>
	public readonly IQFeedLevel1Column BidTime = new("Bid Time", typeof(TimeSpan), _timeFormatMcs) { Field = Level1Fields.BestBidTime };

	/// <summary>
	/// The time of the last date trade.
	/// </summary>
	public readonly IQFeedLevel1Column LastDate = new("Last Date", typeof(DateTime), _dateFormat);

	/// <summary>
	/// The date of the last extended trade.
	/// </summary>
	public readonly IQFeedLevel1Column LastExtendedTradeDate = new("Extended Trade Date", typeof(DateTime), _dateFormat);

	/// <summary>
	/// The most recent trade date.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeDate = new("Most Recent Trade Date", typeof(DateTime), _dateFormat);

	/// <summary>
	/// The most recent trade day code.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeDayCode = new("Most Recent Trade Day Code", typeof(int));

	/// <summary>
	/// The most recent trade aggressor.
	/// 0 - invalid, 1 - buy, 2 - sell, 3 - neither.
	/// </summary>
	public readonly IQFeedLevel1Column MostRecentTradeAggressor = new("Most Recent Trade Aggressor", typeof(int)) { Field = Level1Fields.LastTradeOrigin };

	/// <summary>
	/// Market capitalization.
	/// </summary>
	public readonly IQFeedLevel1Column MarketCapitalization = new("Market Capitalization", typeof(decimal));

	/// <summary>
	/// Price-Earnings Ratio.
	/// </summary>
	public readonly IQFeedLevel1Column PriceEarningsRatio = new("Price-Earnings Ratio", typeof(decimal));

	/// <summary>
	/// Range.
	/// </summary>
	public readonly IQFeedLevel1Column Range = new("Range", typeof(decimal));

	/// <summary>
	/// Spread.
	/// </summary>
	public readonly IQFeedLevel1Column Spread = new("Spread", typeof(decimal));

	/// <summary>
	/// Tick.
	/// "173"=Up, "175"=Down, "183"=No Change. Only valid for Last qualified trades.
	/// </summary>
	public readonly IQFeedLevel1Column Tick = new("Tick", typeof(int));

	/// <summary>
	/// Volatility.
	/// </summary>
	public readonly IQFeedLevel1Column Volatility = new("Volatility", typeof(decimal));
}
