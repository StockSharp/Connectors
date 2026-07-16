namespace StockSharp.KotakNeo.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoOrderSocketLogin
{
	[JsonProperty("type")]
	public string Type { get; set; } = "CONNECTION";

	[JsonProperty("Authorization")]
	public string Authorization { get; set; }

	[JsonProperty("Sid")]
	public string Sid { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; } = "WEB";
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoHeartbeat
{
	[JsonProperty("type")]
	public string Type { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class KotakNeoOrderStreamMessage : KotakNeoOrder
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public KotakNeoOrder Data { get; set; }
}

sealed class KotakNeoDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int? OrdersCount { get; set; }
}

sealed class KotakNeoMarketUpdate
{
	public string FeedType { get; set; }
	public string ExchangeSegment { get; set; }
	public string Token { get; set; }
	public string TradingSymbol { get; set; }
	public DateTime ServerTime { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastVolume { get; set; }
	public decimal? Volume { get; set; }
	public decimal? TotalBuyVolume { get; set; }
	public decimal? TotalSellVolume { get; set; }
	public decimal? BestBidPrice { get; set; }
	public decimal? BestBidVolume { get; set; }
	public decimal? BestAskPrice { get; set; }
	public decimal? BestAskVolume { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? ClosePrice { get; set; }
	public decimal? LowerCircuit { get; set; }
	public decimal? UpperCircuit { get; set; }
	public decimal? YearHigh { get; set; }
	public decimal? YearLow { get; set; }
	public KotakNeoDepthLevel[] Bids { get; set; } = [];
	public KotakNeoDepthLevel[] Asks { get; set; } = [];
}

enum KotakNeoFeedKinds
{
	Scrip,
	Index,
	Depth,
}

sealed class KotakNeoFeedState
{
	public KotakNeoFeedKinds Kind { get; set; }
	public string Topic { get; set; }
	public string ExchangeSegment { get; set; }
	public string Token { get; set; }
	public string TradingSymbol { get; set; }
	public int Multiplier { get; set; } = 1;
	public int Precision { get; set; } = 2;
	public int[] Values { get; } = new int[100];
	public bool[] HasValues { get; } = new bool[100];
}

sealed class KotakNeoFeedDecodeResult
{
	public bool Connected { get; set; }
	public byte[] Acknowledgement { get; set; }
	public KotakNeoMarketUpdate[] Updates { get; set; } = [];
}
