namespace StockSharp.Huobi.Native.Usdt.Model;

class Contract
{
	[JsonProperty("symbol")]
	public string Asset { get; set; }

	[JsonProperty("contract_code")]
	public string ContractCode { get; set; }

	[JsonProperty("contract_size")]
	public double ContractSize { get; set; }

	[JsonProperty("price_tick")]
	public double PriceTick { get; set; }

	[JsonProperty("delivery_date")]
	public string DeliveryDate { get; set; }

	[JsonProperty("delivery_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? DeliveryTime { get; set; }

	[JsonProperty("create_date")]
	public string CreateDate { get; set; }

	[JsonProperty("contract_status")]
	public int ContractStatus { get; set; }

	[JsonProperty("settlement_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? SettlementDate { get; set; }

	[JsonProperty("support_margin_mode")]
	public string SupportMarginMode { get; set; }

	[JsonProperty("business_type")]
	public string BusinessType { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("trade_partition")]
	public string TradePartition { get; set; }
}