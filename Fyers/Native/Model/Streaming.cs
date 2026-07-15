namespace StockSharp.Fyers.Native.Model;

sealed class FyersTokenPayload
{
	[JsonProperty("hsm_key")]
	public string HsmKey { get; set; }

	[JsonProperty("exp")]
	public long ExpiresAt { get; set; }
}

sealed class FyersMarketTick
{
	public string Symbol { get; set; }
	public bool IsDepth { get; set; }
	public DateTime ServerTime { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastVolume { get; set; }
	public decimal? Volume { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? TotalBuyVolume { get; set; }
	public decimal? TotalSellVolume { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? ClosePrice { get; set; }
	public decimal? LowerCircuit { get; set; }
	public decimal? UpperCircuit { get; set; }
	public FyersDepthLevel[] Bids { get; set; } = [];
	public FyersDepthLevel[] Asks { get; set; } = [];
}

sealed class FyersDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int? OrdersCount { get; set; }
	public int? Position { get; set; }
}

sealed class FyersFeedState
{
	public FyersFeedKinds Kind { get; set; }
	public string Topic { get; set; }
	public string Symbol { get; set; }
	public int Multiplier { get; set; } = 1;
	public int Precision { get; set; }
	public int[] Values { get; } = new int[30];
	public bool[] HasValues { get; } = new bool[30];
}

sealed class FyersFeedDecodeResult
{
	public byte[] Acknowledgement { get; set; }
	public FyersMarketTick[] Ticks { get; set; } = [];
}

sealed class FyersOrderSubscriptionRequest
{
	[JsonProperty("T")]
	public string Type { get; set; }

	[JsonProperty("SLIST")]
	public string[] Streams { get; set; }

	[JsonProperty("SUB_T")]
	public int SubscriptionType { get; set; }
}

sealed class FyersOrderStreamMessage
{
	[JsonProperty("s")]
	public FyersResponseStatuses Status { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("orders")]
	public FyersOrderStreamData Order { get; set; }

	[JsonProperty("trades")]
	public FyersTradeStreamData Trade { get; set; }

	[JsonProperty("positions")]
	public FyersPositionStreamData Position { get; set; }
}

sealed class FyersOrderStreamData
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("id_exchange")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("qty_remaining")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("qty_filled")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("price_limit")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("price_stop")]
	public decimal StopPrice { get; set; }

	[JsonProperty("price_traded")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("ord_type")]
	public FyersApiOrderTypes Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("oms_msg")]
	public string Message { get; set; }

	[JsonProperty("offline_flag")]
	public bool IsAfterMarket { get; set; }

	[JsonProperty("time_oms")]
	public string OrderTime { get; set; }

	[JsonProperty("validity")]
	public FyersValidityTypes Validity { get; set; }

	[JsonProperty("product_type")]
	public FyersProducts Product { get; set; }

	[JsonProperty("tran_side")]
	public FyersSides Side { get; set; }

	[JsonProperty("org_ord_status")]
	public FyersOrderStatuses OrderStatus { get; set; }

	[JsonProperty("ordertag")]
	public string OrderTag { get; set; }

	[JsonProperty("fy_token")]
	public string Token { get; set; }
}

sealed class FyersTradeStreamData
{
	[JsonProperty("id_fill")]
	public string TradeId { get; set; }

	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("qty_traded")]
	public decimal Quantity { get; set; }

	[JsonProperty("price_traded")]
	public decimal Price { get; set; }

	[JsonProperty("product_type")]
	public FyersProducts Product { get; set; }

	[JsonProperty("ord_type")]
	public FyersApiOrderTypes OrderType { get; set; }

	[JsonProperty("tran_side")]
	public FyersSides Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fill_time")]
	public string TradeTime { get; set; }

	[JsonProperty("fy_token")]
	public string Token { get; set; }
}

sealed class FyersPositionStreamData
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("net_avg")]
	public decimal NetAverage { get; set; }

	[JsonProperty("net_qty")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("product_type")]
	public FyersProducts Product { get; set; }

	[JsonProperty("pl_realized")]
	public decimal RealizedProfit { get; set; }

	[JsonProperty("pl_unrealized")]
	public decimal UnrealizedProfit { get; set; }

	[JsonProperty("fy_token")]
	public string Token { get; set; }
}

sealed class FyersTbtSubscriptionRequest
{
	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("data")]
	public FyersTbtSubscriptionData Data { get; set; }
}

sealed class FyersTbtSubscriptionData
{
	[JsonProperty("subs")]
	public int SubscriptionType { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }
}

sealed class FyersTbtChannelRequest
{
	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("data")]
	public FyersTbtChannelData Data { get; set; }
}

sealed class FyersTbtChannelData
{
	[JsonProperty("resumeChannels")]
	public string[] ResumeChannels { get; set; }

	[JsonProperty("pauseChannels")]
	public string[] PauseChannels { get; set; }
}

sealed class FyersTbtPacket
{
	public bool IsSnapshot { get; set; }
	public bool IsError { get; set; }
	public string Message { get; set; }
	public FyersTbtFeed[] Feeds { get; set; } = [];
}

sealed class FyersTbtFeed
{
	public string Symbol { get; set; }
	public ulong FeedTime { get; set; }
	public ulong SendTime { get; set; }
	public ulong SequenceNumber { get; set; }
	public bool IsSnapshot { get; set; }
	public FyersTbtDepth Depth { get; set; }
}

sealed class FyersTbtDepth
{
	public ulong? TotalBidQuantity { get; set; }
	public ulong? TotalAskQuantity { get; set; }
	public FyersTbtLevel[] Bids { get; set; } = [];
	public FyersTbtLevel[] Asks { get; set; } = [];
}

sealed class FyersTbtLevel
{
	public long? Price { get; set; }
	public uint? Quantity { get; set; }
	public uint? OrdersCount { get; set; }
	public uint? Number { get; set; }
}

sealed class FyersTbtBook
{
	public FyersDepthLevel[] Bids { get; } = Enumerable.Range(1, 50).Select(i => new FyersDepthLevel { Position = i }).ToArray();
	public FyersDepthLevel[] Asks { get; } = Enumerable.Range(1, 50).Select(i => new FyersDepthLevel { Position = i }).ToArray();
}

sealed class FyersDepthUpdate
{
	public string Symbol { get; set; }
	public DateTime ServerTime { get; set; }
	public FyersDepthLevel[] Bids { get; set; } = [];
	public FyersDepthLevel[] Asks { get; set; } = [];
}
