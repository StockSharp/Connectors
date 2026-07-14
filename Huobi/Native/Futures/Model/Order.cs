namespace StockSharp.Huobi.Native.Futures.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Asset { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("contract_code")]
	public string ContractCode { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("order_price_type")]
	public string OrderPriceType { get; set; }

	[JsonProperty("order_type")]
	public int OrderType { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("offset")]
	public string Offset { get; set; }

	[JsonProperty("lever_rate")]
	public int LeverRate { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("order_id_str")]
	public string OrderIdStr { get; set; }

	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("order_source")]
	public string OrderSource { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("trade_volume")]
	public double? TradeVolume { get; set; }

	[JsonProperty("trade_turnover")]
	public double? TradeTurnover { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("trade_avg_price")]
	public double? TradeAvgPrice { get; set; }

	[JsonProperty("margin_frozen")]
	public double? MarginFrozen { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("trades")]
	public OwnTrade[] Trades { get; set; }
}

class SocketOrder
{
	[JsonProperty("symbol")]
	public string Asset { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("contract_code")]
	public string ContractCode { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("order_price_type")]
	public string OrderPriceType { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("offset")]
	public string Offset { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("lever_rate")]
	public double? LeverRate { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("order_id_str")]
	public string OrderIdStr { get; set; }

	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("order_source")]
	public string OrderSource { get; set; }

	[JsonProperty("order_type")]
	public int OrderType { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("canceled_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CanceledAt { get; set; }

	[JsonProperty("trade_volume")]
	public double? TradeVolume { get; set; }

	[JsonProperty("trade_turnover")]
	public double? TradeTurnover { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("trade_avg_price")]
	public double? TradeAvgPrice { get; set; }

	[JsonProperty("margin_frozen")]
	public double? MarginFrozen { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("is_tpsl")]
	public int IsTpsl { get; set; }

	[JsonProperty("real_profit")]
	public double? RealProfit { get; set; }

	[JsonProperty("trade")]
	public OwnTrade[] Trades { get; set; }
}