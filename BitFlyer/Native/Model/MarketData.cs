namespace StockSharp.BitFlyer.Native.Model;

sealed class BitFlyerMarket
{
	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("market_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerMarketTypes Type { get; set; }
}

sealed class BitFlyerBookLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }
}

sealed class BitFlyerBoard
{
	[JsonProperty("mid_price")]
	public decimal MidPrice { get; set; }

	[JsonProperty("bids")]
	public BitFlyerBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitFlyerBookLevel[] Asks { get; set; }
}

sealed class BitFlyerTicker
{
	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("state")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerMarketStates State { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("tick_id")]
	public long TickId { get; set; }

	[JsonProperty("best_bid")]
	public decimal BestBid { get; set; }

	[JsonProperty("best_ask")]
	public decimal BestAsk { get; set; }

	[JsonProperty("best_bid_size")]
	public decimal BestBidSize { get; set; }

	[JsonProperty("best_ask_size")]
	public decimal BestAskSize { get; set; }

	[JsonProperty("total_bid_depth")]
	public decimal TotalBidDepth { get; set; }

	[JsonProperty("total_ask_depth")]
	public decimal TotalAskDepth { get; set; }

	[JsonProperty("market_bid_size")]
	public decimal MarketBidSize { get; set; }

	[JsonProperty("market_ask_size")]
	public decimal MarketAskSize { get; set; }

	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("volume_by_product")]
	public decimal ProductVolume { get; set; }
}

sealed class BitFlyerPublicExecution
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(BitFlyerNullableSideConverter))]
	public BitFlyerSides? Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("exec_date")]
	public string ExecutionDate { get; set; }

	[JsonProperty("buy_child_order_acceptance_id")]
	public string BuyAcceptanceId { get; set; }

	[JsonProperty("sell_child_order_acceptance_id")]
	public string SellAcceptanceId { get; set; }
}

sealed class BitFlyerProductRequest
{
	public string ProductCode { get; init; }

	public string ToQueryString()
	{
		var builder = new StringBuilder();
		var hasValue = false;
		BitFlyerQueryWriter.Add(builder, ref hasValue, "product_code",
			ProductCode);
		return builder.ToString();
	}
}

sealed class BitFlyerPublicExecutionsRequest
{
	public string ProductCode { get; init; }
	public int? Count { get; init; }
	public long? Before { get; init; }
	public long? After { get; init; }

	public string ToQueryString()
	{
		var builder = new StringBuilder();
		var hasValue = false;
		BitFlyerQueryWriter.Add(builder, ref hasValue, "product_code",
			ProductCode);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "count", Count);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "before", Before);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "after", After);
		return builder.ToString();
	}
}
