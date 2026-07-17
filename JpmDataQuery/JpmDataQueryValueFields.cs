namespace StockSharp.JpmDataQuery;

/// <summary>StockSharp Level1 field receiving the selected DataQuery value.</summary>
public enum JpmDataQueryValueFields
{
	/// <summary>Bid/ask midpoint.</summary>
	SpreadMiddle,

	/// <summary>Last trade price.</summary>
	LastTradePrice,

	/// <summary>Best bid price.</summary>
	BestBidPrice,

	/// <summary>Best ask price.</summary>
	BestAskPrice,

	/// <summary>Open price.</summary>
	OpenPrice,

	/// <summary>High price.</summary>
	HighPrice,

	/// <summary>Low price.</summary>
	LowPrice,

	/// <summary>Close price.</summary>
	ClosePrice,

	/// <summary>Volume.</summary>
	Volume,

	/// <summary>Open interest.</summary>
	OpenInterest,

	/// <summary>Settlement price.</summary>
	SettlementPrice,
}
