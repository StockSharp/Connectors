namespace StockSharp.Amberdata.Native.Model;

sealed class AmberdataTrade
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public long? ExchangeTimestamp { get; set; }

	[JsonProperty("exchangeTimestampNanoseconds")]
	public int? ExchangeTimestampNanoseconds { get; set; }

	[JsonProperty("isBuySide")]
	public bool? IsBuySide { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }
}

sealed class AmberdataTicker
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public long? ExchangeTimestamp { get; set; }

	[JsonProperty("exchangeTimestampNanoseconds")]
	public int? ExchangeTimestampNanoseconds { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("mid")]
	public decimal? Mid { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("lastVolume")]
	public decimal? LastVolume { get; set; }

	[JsonProperty("bidVolume")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("askVolume")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("open24H")]
	public decimal? OpenOneDay { get; set; }

	[JsonProperty("low24H")]
	public decimal? LowOneDay { get; set; }

	[JsonProperty("high24H")]
	public decimal? HighOneDay { get; set; }

	[JsonProperty("sequence")]
	public string Sequence { get; set; }
}

sealed class AmberdataBookLevel
{
	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("numOrders")]
	public int? OrdersCount { get; set; }
}

sealed class AmberdataBookSnapshot
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("sequence")]
	public string Sequence { get; set; }

	[JsonProperty("ask")]
	public AmberdataBookLevel[] Asks { get; set; }

	[JsonProperty("bid")]
	public AmberdataBookLevel[] Bids { get; set; }
}

sealed class AmberdataOhlcv
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public long? ExchangeTimestamp { get; set; }

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
}
