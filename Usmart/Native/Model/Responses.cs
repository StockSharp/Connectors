namespace StockSharp.Usmart.Native.Model;

[DataContract]
class UsmartResponse
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

[DataContract]
sealed class UsmartResponse<T> : UsmartResponse
{
	[JsonProperty("data")]
	public T Data { get; set; }
}

[DataContract]
sealed class UsmartListData<T>
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("latestTime")]
	public long LatestTime { get; set; }

	[JsonProperty("list")]
	public T[] Items { get; set; }
}

[DataContract]
sealed class UsmartPage<T>
{
	[JsonProperty("pageNum")]
	public int Page { get; set; }

	[JsonProperty("pageSize")]
	public int PageSize { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("list")]
	public T[] Items { get; set; }
}

[DataContract]
sealed class UsmartSecurity
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("nameChs")]
	public string SimplifiedName { get; set; }

	[JsonProperty("nameCht")]
	public string TraditionalName { get; set; }

	[JsonProperty("nameEn")]
	public string EnglishName { get; set; }

	[JsonProperty("type1")]
	public int Type { get; set; }

	[JsonProperty("lotSize")]
	public decimal LotSize { get; set; }
}

[DataContract]
sealed class UsmartMarketState
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("desc")]
	public string Description { get; set; }

	[JsonProperty("tradingDayType")]
	public int TradingDayType { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }
}

[DataContract]
sealed class UsmartQuote
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("latestPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("latestTime")]
	public long LatestTime { get; set; }

	[JsonProperty("preClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("turnOver")]
	public decimal? Turnover { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("upLimit")]
	public decimal? UpperLimit { get; set; }

	[JsonProperty("downLimit")]
	public decimal? LowerLimit { get; set; }

	[JsonProperty("qtyUnit")]
	public decimal? PriceStep { get; set; }

	[JsonProperty("trdStatus")]
	public int TradingStatus { get; set; }
}

[DataContract]
sealed class UsmartCandle
{
	[JsonProperty("latestTime")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal? Turnover { get; set; }
}

[DataContract]
sealed class UsmartTick
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("direction")]
	public int Direction { get; set; }

	[JsonProperty("trdType")]
	public int TradeType { get; set; }
}

[DataContract]
sealed class UsmartDepthLevel
{
	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidVolume")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("bidOrderCount")]
	public int? BidOrders { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askVolume")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("askOrderCount")]
	public int? AskOrders { get; set; }
}

[DataContract]
sealed class UsmartOrderAction
{
	[JsonProperty("entrustId")]
	public string OrderId { get; set; }

	[JsonProperty("oddId")]
	public string FractionalOrderId { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("statusName")]
	public string StatusName { get; set; }
}

[DataContract]
sealed class UsmartOrder
{
	[JsonProperty("businessAmount")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("businessAveragePrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("serialNo")]
	public long SerialNo { get; set; }

	[JsonProperty("createDate")]
	public string CreateDate { get; set; }

	[JsonProperty("createTime")]
	public string CreateTime { get; set; }

	[JsonProperty("entrustAmount")]
	public decimal Quantity { get; set; }

	[JsonProperty("entrustId")]
	public string OrderId { get; set; }

	[JsonProperty("entrustNo")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("entrustPrice")]
	public decimal Price { get; set; }

	[JsonProperty("entrustProp")]
	public string Instruction { get; set; }

	[JsonProperty("entrustType")]
	public int Side { get; set; }

	[JsonProperty("exchangeType")]
	public int Exchange { get; set; }

	[JsonProperty("flag")]
	public string Flag { get; set; }

	[JsonProperty("moneyType")]
	public int Currency { get; set; }

	[JsonProperty("sessionType")]
	public int Session { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("statusName")]
	public string StatusName { get; set; }

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }

	[JsonProperty("stockName")]
	public string StockName { get; set; }
}

[DataContract]
sealed class UsmartTrade
{
	[JsonProperty("recordId")]
	public long RecordId { get; set; }

	[JsonProperty("entrustId")]
	public long OrderId { get; set; }

	[JsonProperty("businessStatus")]
	public int Status { get; set; }

	[JsonProperty("businessPrice")]
	public decimal Price { get; set; }

	[JsonProperty("businessAmount")]
	public decimal Quantity { get; set; }

	[JsonProperty("businessBalance")]
	public decimal Amount { get; set; }

	[JsonProperty("businessTime")]
	public string Time { get; set; }

	[JsonProperty("entrustType")]
	public int Side { get; set; }

	[JsonProperty("exchangeType")]
	public int Exchange { get; set; }

	[JsonProperty("moneyType")]
	public int Currency { get; set; }

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }

	[JsonProperty("stockName")]
	public string StockName { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }
}

[DataContract]
sealed class UsmartHolding
{
	[JsonProperty("costPrice")]
	public string CostPrice { get; set; }

	[JsonProperty("costPriceAccurate")]
	public string AccurateCostPrice { get; set; }

	[JsonProperty("currentAmount")]
	public string Quantity { get; set; }

	[JsonProperty("enableAmount")]
	public string AvailableQuantity { get; set; }

	[JsonProperty("frozenAmount")]
	public string FrozenQuantity { get; set; }

	[JsonProperty("exchangeType")]
	public int Exchange { get; set; }

	[JsonProperty("oddAmount")]
	public string FractionalQuantity { get; set; }

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }

	[JsonProperty("stockName")]
	public string StockName { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("marketValue")]
	public string MarketValue { get; set; }

	[JsonProperty("holdingBalance")]
	public string UnrealizedPnL { get; set; }
}

[DataContract]
sealed class UsmartAsset
{
	[JsonProperty("asset")]
	public string TotalAsset { get; set; }

	[JsonProperty("enableBalance")]
	public string AvailableCash { get; set; }

	[JsonProperty("withdrawBalance")]
	public string WithdrawableCash { get; set; }

	[JsonProperty("frozenBalance")]
	public string FrozenCash { get; set; }

	[JsonProperty("marketValue")]
	public string MarketValue { get; set; }

	[JsonProperty("moneyType")]
	public int? Currency { get; set; }

	[JsonProperty("stockHoldingList")]
	public UsmartHolding[] Holdings { get; set; }
}
