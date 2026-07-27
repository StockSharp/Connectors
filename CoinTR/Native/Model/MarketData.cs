namespace StockSharp.CoinTR.Native.Model;

sealed class CoinTRSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("minTradeAmount")]
	public decimal? MinimumTradeAmount { get; set; }

	[JsonProperty("maxTradeAmount")]
	public decimal? MaximumTradeAmount { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonIgnore]
	public string SecurityCode
		=> CoinTRExtensions.CreateSecurityCode(BaseCoin, QuoteCoin);
}

sealed class CoinTRTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	private string InstrumentId
	{
		set
		{
			if (!value.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("high24h")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("open24h")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("low24h")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("lastPr")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("baseVolume")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("usdtVolume")]
	public decimal? UsdtVolume { get; set; }

	[JsonProperty("bidPr")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPr")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bidSz")]
	public decimal? BidSize { get; set; }

	[JsonProperty("askSz")]
	public decimal? AskSize { get; set; }

	[JsonProperty("openUtc")]
	public decimal? OpenUtc { get; set; }

	[JsonProperty("changeUtc24h")]
	public decimal? ChangeUtc24h { get; set; }

	[JsonProperty("change24h")]
	public decimal? Change24h { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinTRTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinTROrderBook
{
	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public CoinTRQuote[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinTRQuote[] Bids { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
sealed class CoinTRQuote
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
sealed class CoinTRCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal BaseVolume { get; set; }
	public decimal UsdtVolume { get; set; }
	public decimal QuoteVolume { get; set; }
}
