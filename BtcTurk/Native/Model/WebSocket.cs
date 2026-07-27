namespace StockSharp.BtcTurk.Native.Model;

sealed class BtcTurkWsEnvelope<TData>
{
	public BtcTurkWsMessageTypes Type { get; init; }
	public TData Data { get; init; }
}

sealed class BtcTurkWsSubscription
{
	[JsonProperty("type")]
	public BtcTurkWsMessageTypes Type { get; init; } =
		BtcTurkWsMessageTypes.Subscription;

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("join")]
	public bool IsSubscribe { get; init; }
}

sealed class BtcTurkWsResult
{
	[JsonProperty("type")]
	public BtcTurkWsMessageTypes Type { get; set; }

	[JsonProperty("ok")]
	public bool IsSuccess { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BtcTurkWsPriceLevel
{
	[JsonProperty("P")]
	public decimal Price { get; set; }

	[JsonProperty("A")]
	public decimal Volume { get; set; }
}

sealed class BtcTurkWsOrderBook
{
	[JsonProperty("CS")]
	public long ChangeSet { get; set; }

	[JsonProperty("PS")]
	public string PairSymbol { get; set; }

	[JsonProperty("AO")]
	public BtcTurkWsPriceLevel[] Asks { get; set; }

	[JsonProperty("BO")]
	public BtcTurkWsPriceLevel[] Bids { get; set; }
}

sealed class BtcTurkWsTrade
{
	[JsonProperty("PS")]
	public string PairSymbol { get; set; }

	[JsonProperty("A")]
	public decimal Amount { get; set; }

	[JsonProperty("S")]
	public int RawSide { get; set; }

	[JsonProperty("D")]
	public long Timestamp { get; set; }

	[JsonProperty("P")]
	public decimal Price { get; set; }

	[JsonProperty("I")]
	public string Id { get; set; }

	[JsonIgnore]
	public BtcTurkSides Side
		=> RawSide == 0 ? BtcTurkSides.Buy : BtcTurkSides.Sell;
}

sealed class BtcTurkWsTradeHistory
{
	[JsonProperty("symbol")]
	public string PairSymbol { get; set; }

	[JsonProperty("items")]
	public BtcTurkWsTrade[] Items { get; set; }
}

sealed class BtcTurkWsTicker
{
	[JsonProperty("PS")]
	public string PairSymbol { get; set; }

	[JsonProperty("H")]
	public decimal? High { get; set; }

	[JsonProperty("L")]
	public decimal? Low { get; set; }

	[JsonProperty("LA")]
	public decimal? Last { get; set; }

	[JsonProperty("V")]
	public decimal? Volume { get; set; }

	[JsonProperty("AV")]
	public decimal? Average { get; set; }

	[JsonProperty("D")]
	public decimal? Daily { get; set; }

	[JsonProperty("DP")]
	public decimal? DailyPercent { get; set; }

	[JsonProperty("O")]
	public decimal? Open { get; set; }

	[JsonProperty("B")]
	public decimal? Bid { get; set; }

	[JsonProperty("A")]
	public decimal? Ask { get; set; }

	[JsonProperty("BA")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("AA")]
	public decimal? AskVolume { get; set; }
}
