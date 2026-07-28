namespace StockSharp.CoinSwitch.Native.Model;

sealed class CoinSwitchQuoteRange
{
	[JsonProperty("min")]
	public decimal? Minimum { get; set; }

	[JsonProperty("max")]
	public decimal? Maximum { get; set; }
}

sealed class CoinSwitchPrecision
{
	[JsonProperty("base")]
	public int Base { get; set; }

	[JsonProperty("quote")]
	public int Quote { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

sealed class CoinSwitchSpotTradeInfo
{
	[JsonProperty("quote")]
	public CoinSwitchQuoteRange Quote { get; set; }

	[JsonProperty("precision")]
	public CoinSwitchPrecision Precision { get; set; }

	[JsonIgnore]
	public decimal? VolumeStep
		=> CoinSwitchExtensions.GetStep(Precision?.Base ?? -1);

	[JsonIgnore]
	public decimal? PriceStep
		=> CoinSwitchExtensions.GetStep(Precision?.Quote ?? -1);

	[JsonIgnore]
	public decimal? MinimumQuote => Quote?.Minimum;

	[JsonIgnore]
	public decimal? MaximumQuote => Quote?.Maximum;
}

sealed class CoinSwitchTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("baseVolume")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("percentageChange")]
	public decimal? PercentageChange { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("at")]
	public long Timestamp { get; set; }
}

sealed class CoinSwitchDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }
}

sealed class CoinSwitchTrade
{
	[JsonProperty("E")]
	public long Timestamp { get; set; }

	[JsonProperty("m")]
	public bool BuyerMaker { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public JToken TradeIdValue { get; set; }

	[JsonProperty("e")]
	public string Exchange { get; set; }

	[JsonIgnore]
	public string TradeId => TradeIdValue?.ToString(
		Formatting.None).Trim('"');

	[JsonIgnore]
	public Sides OriginSide
		=> BuyerMaker ? Sides.Sell : Sides.Buy;
}

sealed class CoinSwitchCandle
{
	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal SpotVolume { get; set; }

	[JsonProperty("v")]
	public decimal FuturesVolume { get; set; }

	[JsonProperty("q")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("symbol")]
	private string SpotSymbol
	{
		set => Symbol = value;
	}

	[JsonProperty("s")]
	private string FuturesSymbol
	{
		set => Symbol = value;
	}

	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	private string SpotInterval
	{
		set => Interval = value;
	}

	[JsonProperty("i")]
	private string FuturesInterval
	{
		set => Interval = value;
	}

	[JsonIgnore]
	public string Interval { get; set; }

	[JsonProperty("start_time")]
	private JToken SpotStart
	{
		set => StartTime = value?.Value<long>() ?? 0;
	}

	[JsonProperty("t")]
	private long FuturesStart
	{
		set => StartTime = value;
	}

	[JsonIgnore]
	public long StartTime { get; set; }

	[JsonProperty("close_time")]
	private JToken SpotClose
	{
		set => CloseTime = value?.Value<long>() ?? 0;
	}

	[JsonProperty("end_time")]
	private JToken SpotEnd
	{
		set => CloseTime = value?.Value<long>() ?? 0;
	}

	[JsonProperty("T")]
	private long FuturesClose
	{
		set => CloseTime = value;
	}

	[JsonIgnore]
	public long CloseTime { get; set; }

	[JsonProperty("x")]
	public bool? IsClosed { get; set; }

	[JsonIgnore]
	public decimal Volume
		=> FuturesVolume != 0 ? FuturesVolume : SpotVolume;
}

sealed class CoinSwitchHftTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("highPrice24h")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice24h")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("bid1Price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid1Size")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("ask1Price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask1Size")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("volume24h")]
	public decimal? Volume { get; set; }

	[JsonProperty("turnover24h")]
	public decimal? Turnover { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }
}

sealed class CoinSwitchHftOrderBook
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("a")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("u")]
	public long UpdateId { get; set; }
}

sealed class CoinSwitchHftTrade
{
	[JsonProperty("execId")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Volume { get; set; }

	[JsonProperty("time")]
	public long Timestamp { get; set; }
}

sealed class CoinSwitchHftCandle
{
	public long OpenTime { get; init; }

	public decimal Open { get; init; }

	public decimal High { get; init; }

	public decimal Low { get; init; }

	public decimal Close { get; init; }

	public decimal Volume { get; init; }

	public decimal Turnover { get; init; }
}
