namespace StockSharp.ZeroHash.Native.Model;

sealed class ZeroHashMarketSubscriptionRequest
{
	[JsonProperty("depth")]
	public int Depth { get; set; }

	[JsonProperty("snapshotOnly")]
	public bool IsSnapshotOnly { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("unaggregated")]
	public bool IsUnaggregated { get; set; }
}

sealed class ZeroHashMarketEnvelope
{
	[JsonProperty("error")]
	public ZeroHashApiError Error { get; set; }

	[JsonProperty("result")]
	public ZeroHashMarketResult Result { get; set; }
}

sealed class ZeroHashMarketResult
{
	[JsonProperty("update")]
	public ZeroHashMarketUpdate Update { get; set; }
}

sealed class ZeroHashMarketUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("transactTime")]
	public string TransactionTime { get; set; }

	[JsonProperty("bids")]
	public ZeroHashBookLevel[] Bids { get; set; }

	[JsonProperty("offers")]
	public ZeroHashBookLevel[] Offers { get; set; }

	[JsonProperty("bookHidden")]
	public bool IsBookHidden { get; set; }

	[JsonProperty("state")]
	public ZeroHashInstrumentStates? State { get; set; }

	[JsonProperty("stats")]
	public ZeroHashMarketStats Statistics { get; set; }
}

sealed class ZeroHashBookLevel
{
	[JsonProperty("px")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("symbolSubType")]
	public string SymbolSubType { get; set; }
}

sealed class ZeroHashMarketStats
{
	[JsonProperty("openPx")]
	public string OpenPrice { get; set; }

	[JsonProperty("highPx")]
	public string HighPrice { get; set; }

	[JsonProperty("lowPx")]
	public string LowPrice { get; set; }

	[JsonProperty("closePx")]
	public string ClosePrice { get; set; }

	[JsonProperty("lastTradePx")]
	public string LastTradePrice { get; set; }

	[JsonProperty("lastTradeQty")]
	public string LastTradeQuantity { get; set; }

	[JsonProperty("lastTradeSetTime")]
	public string LastTradeTime { get; set; }

	[JsonProperty("sharesTraded")]
	public string SharesTraded { get; set; }

	[JsonProperty("notionalTraded")]
	public string NotionalTraded { get; set; }

	[JsonProperty("settlementPx")]
	public string SettlementPrice { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }
}
