namespace StockSharp.NovaDax.Native.Model;

class NovaDaxSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("amountPrecision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("valuePrecision")]
	public int ValuePrecision { get; set; }

	[JsonProperty("minOrderAmount")]
	public decimal? MinimumAmount { get; set; }

	[JsonProperty("minOrderValue")]
	public decimal? MinimumValue { get; set; }

	[JsonIgnore]
	public string Pair
	{
		get => Symbol;
		set => Symbol = value;
	}

	[JsonIgnore]
	public string Base => BaseCurrency;

	[JsonIgnore]
	public string Quote => QuoteCurrency;

	[JsonIgnore]
	public string SecurityCode => Symbol.ToNovaDaxSecurityCode();

	[JsonIgnore]
	public decimal? PriceStep
		=> NovaDaxExtensions.GetStep(QuotePrecision);

	[JsonIgnore]
	public decimal? AmountStep
		=> NovaDaxExtensions.GetStep(AmountPrecision);

	[JsonIgnore]
	public decimal? MaximumAmount => null;

	[JsonIgnore]
	public bool IsMaintenance => false;
}

sealed class NovaDaxTicker
{
	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("open24h")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high24h")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low24h")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("baseVolume24h")]
	public decimal? Volume { get; set; }

	[JsonProperty("quoteVolume24h")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public decimal? BidPrice => Bid;

	[JsonIgnore]
	public decimal? BidVolume => null;

	[JsonIgnore]
	public decimal? AskPrice => Ask;

	[JsonIgnore]
	public decimal? AskVolume => null;

	[JsonIgnore]
	public decimal? PriceChange
		=> LastPrice is decimal last &&
			OpenPrice is decimal open
				? last - open
				: null;

	[JsonIgnore]
	public bool? IsBuyer => null;
}

sealed class NovaDaxOrderBook
{
	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public string Pair { get; set; }

	[JsonIgnore]
	public int Limit { get; set; }
}

sealed class NovaDaxTrade
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public string Pair { get; set; }

	[JsonIgnore]
	public long Id => 0;

	[JsonIgnore]
	public bool? IsBuyer => Side.EqualsIgnoreCase("BUY");
}

sealed class NovaDaxTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public NovaDaxTrade[] Data { get; set; }
}

sealed class NovaDaxCandle
{
	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("score")]
	public long Timestamp { get; set; }

	[JsonProperty("openPrice")]
	public decimal Open { get; set; }

	[JsonProperty("highPrice")]
	public decimal High { get; set; }

	[JsonProperty("lowPrice")]
	public decimal Low { get; set; }

	[JsonProperty("closePrice")]
	public decimal Close { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("vol")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("count")]
	public long Count { get; set; }

	[JsonIgnore]
	public decimal Volume => Amount;

	[JsonIgnore]
	public bool IsFinished { get; set; } = true;
}
