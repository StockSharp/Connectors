namespace StockSharp.StocksTrader.Native.Model;

sealed class StocksTraderEnvelope<T>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class StocksTraderAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("system")]
	public string System { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class StocksTraderAccountMarginState
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("unrealized_pl")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("free_margin")]
	public decimal FreeMargin { get; set; }
}

sealed class StocksTraderAccountCashState
{
	[JsonProperty("my_portfolio")]
	public decimal MyPortfolio { get; set; }

	[JsonProperty("investments")]
	public decimal Investments { get; set; }

	[JsonProperty("available_to_invest")]
	public decimal AvailableToInvest { get; set; }
}

sealed class StocksTraderAccountState
{
	[JsonProperty("cash")]
	public StocksTraderAccountCashState Cash { get; set; }

	[JsonProperty("margin")]
	public StocksTraderAccountMarginState Margin { get; set; }
}

sealed class StocksTraderInstrument
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("contract_size")]
	public decimal ContractSize { get; set; }

	[JsonProperty("units")]
	public string Units { get; set; }

	[JsonProperty("min_volume")]
	public decimal MinVolume { get; set; }

	[JsonProperty("max_volume")]
	public decimal MaxVolume { get; set; }

	[JsonProperty("volume_step")]
	public decimal VolumeStep { get; set; }

	[JsonProperty("min_tick")]
	public decimal MinTick { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("trade_mode")]
	public string TradeMode { get; set; }
}

sealed class StocksTraderQuote
{
	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("ask_bid_price_time")]
	public long? AskBidPriceTime { get; set; }

	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("last_price_time")]
	public long? LastPriceTime { get; set; }
}

sealed class StocksTraderOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("filled_price")]
	public decimal? FilledPrice { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("expiration")]
	public long? Expiration { get; set; }

	[JsonProperty("last_modified")]
	public long? LastModified { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }

	[JsonProperty("create_time")]
	public long? CreateTime { get; set; }

	[JsonProperty("deals")]
	public string[] Deals { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class StocksTraderDeal
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("open_price")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("open_time")]
	public long? OpenTime { get; set; }

	[JsonProperty("profit")]
	public decimal Profit { get; set; }

	[JsonProperty("close_price")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("close_time")]
	public long? CloseTime { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class StocksTraderOrderResult
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class StocksTraderOrderRequest
{
	public string Ticker { get; set; }
	public decimal? Volume { get; set; }
	public string Side { get; set; }
	public string Type { get; set; }
	public decimal? Price { get; set; }
	public long? Expiration { get; set; }
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
}

sealed class StocksTraderModifyOrderRequest
{
	public decimal? Volume { get; set; }
	public decimal? Price { get; set; }
	public long? Expiration { get; set; }
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
}

sealed class StocksTraderModifyDealRequest
{
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
}

sealed class StocksTraderHistoryQuery
{
	public DateTime? From { get; set; }
	public DateTime? To { get; set; }
	public long? Skip { get; set; }
	public int? Limit { get; set; }
}
