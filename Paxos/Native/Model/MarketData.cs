namespace StockSharp.Paxos.Native.Model;

using Newtonsoft.Json.Linq;

sealed class PaxosMarketsResponse
{
	[JsonProperty("markets")]
	public PaxosMarket[] Markets { get; set; }
}

sealed class PaxosMarket
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("tick_rate")]
	public string TickRate { get; set; }

	[JsonProperty("min_base_amount")]
	public string MinimumBaseAmount { get; set; }

	[JsonProperty("min_quote_amount")]
	public string MinimumQuoteAmount { get; set; }

	[JsonProperty("max_base_amount")]
	public string MaximumBaseAmount { get; set; }

	[JsonProperty("max_quote_amount")]
	public string MaximumQuoteAmount { get; set; }

	[JsonProperty("market_status")]
	public PaxosMarketStatus MarketStatus { get; set; }
}

sealed class PaxosMarketStatus
{
	[JsonProperty("buy_status")]
	public PaxosMarketTradingStatuses BuyStatus { get; set; }

	[JsonProperty("sell_status")]
	public PaxosMarketTradingStatuses SellStatus { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

sealed class PaxosBookLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class PaxosPublicExecution
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("executed_at")]
	public string ExecutedAt { get; set; }
}

sealed class PaxosCandle
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class PaxosCandlesResponse
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("increment")]
	public PaxosCandleIncrements Increment { get; set; }

	[JsonProperty("items")]
	public PaxosCandle[] Items { get; set; }

	[JsonProperty("next_page_cursor")]
	public string NextPageCursor { get; set; }
}

sealed class PaxosSocketEnvelope
{
	[JsonProperty("type")]
	public PaxosSocketOperations? Type { get; set; }

	[JsonProperty("channel")]
	public PaxosSocketFeeds? Channel { get; set; }

	[JsonProperty("channels")]
	public PaxosSocketChannel[] Channels { get; set; }

	[JsonProperty("payload")]
	public JObject Payload { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class PaxosSocketChannel
{
	[JsonProperty("type")]
	public PaxosSocketFeeds Type { get; set; }

	[JsonProperty("params")]
	public PaxosSocketParameters Parameters { get; set; }

	[JsonProperty("success")]
	public bool? Success { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class PaxosSocketParameters
{
	[JsonProperty("market")]
	public string Market { get; set; }
}

sealed class PaxosBookMessage
{
	[JsonProperty("type")]
	public PaxosSocketMessageTypes Type { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("bids")]
	public PaxosBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public PaxosBookLevel[] Asks { get; set; }
}
