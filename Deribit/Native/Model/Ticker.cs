namespace StockSharp.Deribit.Native.Model;

class TickerGreeks
{
	[JsonProperty("delta")]
	public double? Delta { get; set; }

	[JsonProperty("gamma")]
	public double? Gamma { get; set; }

	[JsonProperty("rho")]
	public double? Rho { get; set; }

	[JsonProperty("theta")]
	public double? Theta { get; set; }

	[JsonProperty("vega")]
	public double? Vega { get; set; }
}

class TickerStats
{
	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }
}

class Ticker
{
	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("stats")]
	public TickerStats Stats { get; set; }

	[JsonProperty("greeks")]
	public TickerGreeks Greeks { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("settlement_price")]
	public double? SettlementPrice { get; set; }

	[JsonProperty("open_interest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("min_price")]
	public double? MinPrice { get; set; }

	[JsonProperty("max_price")]
	public double? MaxPrice { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("mark_iv")]
	public double? MarkIv { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("funding_8h")]
	public double? Funding8H { get; set; }

	[JsonProperty("current_funding")]
	public double? CurrentFunding { get; set; }

	[JsonProperty("best_bid_price")]
	public double? BestBidPrice { get; set; }

	[JsonProperty("best_bid_amount")]
	public double? BestBidAmount { get; set; }

	[JsonProperty("best_ask_price")]
	public double? BestAskPrice { get; set; }

	[JsonProperty("best_ask_amount")]
	public double? BestAskAmount { get; set; }
}