namespace StockSharp.Tardis.Native.Model;

sealed class TardisMachineOptions
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("dataTypes")]
	public string[] DataTypes { get; set; }

	[JsonProperty("withDisconnectMessages")]
	public bool? IsWithDisconnectMessages { get; set; }

	[JsonProperty("withErrorMessages")]
	public bool? IsWithErrorMessages { get; set; }

	[JsonProperty("timeoutIntervalMS")]
	public int? TimeoutIntervalMilliseconds { get; set; }
}

class TardisNormalizedMessage
{
	[JsonProperty("type")]
	public TardisMessageTypes Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("localTimestamp")]
	public string LocalTimestamp { get; set; }
}

sealed class TardisTrade : TardisNormalizedMessage
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("side")]
	public TardisSides Side { get; set; }
}

sealed class TardisBookLevel
{
	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }
}

sealed class TardisBookChange : TardisNormalizedMessage
{
	[JsonProperty("isSnapshot")]
	public bool IsSnapshot { get; set; }

	[JsonProperty("bids")]
	public TardisBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public TardisBookLevel[] Asks { get; set; }
}

sealed class TardisBookTicker : TardisNormalizedMessage
{
	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askAmount")]
	public decimal? AskAmount { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidAmount")]
	public decimal? BidAmount { get; set; }
}

sealed class TardisDerivativeTicker : TardisNormalizedMessage
{
	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("fundingTimestamp")]
	public string FundingTimestamp { get; set; }

	[JsonProperty("predictedFundingRate")]
	public decimal? PredictedFundingRate { get; set; }
}

sealed class TardisBookSnapshot : TardisNormalizedMessage
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("depth")]
	public int Depth { get; set; }

	[JsonProperty("interval")]
	public long Interval { get; set; }

	[JsonProperty("grouping")]
	public decimal? Grouping { get; set; }

	[JsonProperty("bids")]
	public TardisBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public TardisBookLevel[] Asks { get; set; }
}

sealed class TardisTradeBar : TardisNormalizedMessage
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("interval")]
	public long Interval { get; set; }

	[JsonProperty("kind")]
	public TardisBarKinds Kind { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("buyVolume")]
	public decimal? BuyVolume { get; set; }

	[JsonProperty("sellVolume")]
	public decimal? SellVolume { get; set; }

	[JsonProperty("trades")]
	public long? Trades { get; set; }

	[JsonProperty("vwap")]
	public decimal? VolumeWeightedAveragePrice { get; set; }

	[JsonProperty("openTimestamp")]
	public string OpenTimestamp { get; set; }

	[JsonProperty("closeTimestamp")]
	public string CloseTimestamp { get; set; }
}

sealed class TardisDisconnect : TardisNormalizedMessage
{
}

sealed class TardisMachineError : TardisNormalizedMessage
{
	[JsonProperty("details")]
	public string Details { get; set; }

	[JsonProperty("subSequentErrorsCount")]
	public int SubsequentErrorsCount { get; set; }
}
