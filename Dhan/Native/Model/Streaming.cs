namespace StockSharp.Dhan.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanSubscriptionRequest
{
	[JsonProperty("RequestCode")]
	public int RequestCode { get; set; }

	[JsonProperty("InstrumentCount")]
	public int InstrumentCount { get; set; }

	[JsonProperty("InstrumentList")]
	public DhanSubscriptionInstrument[] Instruments { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanSingleSubscriptionRequest
{
	[JsonProperty("RequestCode")]
	public int RequestCode { get; set; }

	[JsonProperty("ExchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("SecurityId")]
	public string SecurityId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanSubscriptionInstrument
{
	[JsonProperty("ExchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("SecurityId")]
	public string SecurityId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanDisconnectRequest
{
	[JsonProperty("RequestCode")]
	public int RequestCode { get; set; } = 12;
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderLogin
{
	[JsonProperty("LoginReq")]
	public DhanOrderLoginRequest Login { get; set; }

	[JsonProperty("UserType")]
	public string UserType { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderLoginRequest
{
	[JsonProperty("MsgCode")]
	public int MessageCode { get; set; }

	[JsonProperty("ClientId")]
	public string ClientId { get; set; }

	[JsonProperty("Token")]
	public string Token { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderUpdate
{
	[JsonProperty("Type")]
	public string Type { get; set; }

	[JsonProperty("Data")]
	public DhanOrderUpdateData Data { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderUpdateData
{
	[JsonProperty("Exchange")]
	public string Exchange { get; set; }

	[JsonProperty("Segment")]
	public string Segment { get; set; }

	[JsonProperty("SecurityId")]
	public string SecurityId { get; set; }

	[JsonProperty("ExchOrderNo")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("OrderNo")]
	public string OrderId { get; set; }

	[JsonProperty("Product")]
	public string Product { get; set; }

	[JsonProperty("ProductName")]
	public string ProductName { get; set; }

	[JsonProperty("TxnType")]
	public string TransactionType { get; set; }

	[JsonProperty("OrderType")]
	public string OrderType { get; set; }

	[JsonProperty("Validity")]
	public string Validity { get; set; }

	[JsonProperty("DiscQuantity")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("RemainingQuantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("Quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("TradedQty")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("TriggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("TradedPrice")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("AvgTradedPrice")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("OffMktFlag")]
	public string AfterMarketFlag { get; set; }

	[JsonProperty("OrderDateTime")]
	public string OrderTime { get; set; }

	[JsonProperty("ExchOrderTime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("LastUpdatedTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("ReasonDescription")]
	public string Reason { get; set; }

	[JsonProperty("LegNo")]
	public int LegNumber { get; set; }

	[JsonProperty("Instrument")]
	public string Instrument { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("Status")]
	public string Status { get; set; }

	[JsonProperty("CorrelationId")]
	public string CorrelationId { get; set; }
}

sealed class DhanDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int OrdersCount { get; set; }
}

sealed class DhanMarketTick
{
	public DhanExchangeSegments ExchangeSegment { get; set; }
	public string SecurityId { get; set; }
	public DateTime ServerTime { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastVolume { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? Volume { get; set; }
	public decimal? TotalBuyVolume { get; set; }
	public decimal? TotalSellVolume { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? ClosePrice { get; set; }
	public DhanDepthLevel[] Bids { get; set; } = [];
	public DhanDepthLevel[] Asks { get; set; } = [];
}

sealed class DhanDepthUpdate
{
	public DhanExchangeSegments ExchangeSegment { get; set; }
	public string SecurityId { get; set; }
	public DhanDepthLevel[] Bids { get; set; } = [];
	public DhanDepthLevel[] Asks { get; set; } = [];
}
