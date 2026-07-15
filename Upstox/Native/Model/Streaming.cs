namespace StockSharp.Upstox.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxFeedRequest
{
	[JsonProperty("guid")]
	public string Guid { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("data")]
	public UpstoxFeedRequestData Data { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxFeedRequestData
{
	[JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
	public string Mode { get; set; }

	[JsonProperty("instrumentKeys")]
	public string[] InstrumentKeys { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxPortfolioUpdate
{
	[JsonProperty("update_type")]
	public string UpdateType { get; set; }

	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument_token")]
	public string InstrumentToken { get; set; }

	[JsonProperty("instrument_key")]
	public string InstrumentKey { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("trigger_price")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal? PendingQuantity { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("status_message")]
	public string StatusMessage { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }

	[JsonProperty("exchange_timestamp")]
	public string ExchangeTimestamp { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("buy_value")]
	public decimal? BuyValue { get; set; }

	[JsonProperty("sell_value")]
	public decimal? SellValue { get; set; }

	[JsonProperty("pnl")]
	public decimal? PnL { get; set; }

	[JsonProperty("unrealised")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("realised")]
	public decimal? RealizedPnL { get; set; }
}
