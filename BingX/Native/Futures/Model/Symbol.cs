namespace StockSharp.BingX.Native.Futures.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

	[JsonProperty("contractId")]
	public long? ContractId { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("feeRate")]
	public double? FeeRate { get; set; }

	[JsonProperty("tradeMinQuantity")]
	public double? TradeMinQuantity { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("apiStateOpen")]
	public bool ApiStateOpen { get; set; }

	[JsonProperty("apiStatePlaceOrder")]
	public bool ApiStatePlaceOrder { get; set; }

	[JsonProperty("apiStateCancelOrder")]
	public bool ApiStateCancelOrder { get; set; }

	[JsonProperty("maintMarginRatio")]
	public double? MaintenanceMarginRatio { get; set; }

	[JsonProperty("requiredMarginRatio")]
	public double? RequiredMarginRatio { get; set; }

	[JsonProperty("priceScale")]
	public double? PriceScale { get; set; }

	[JsonProperty("volScale")]
	public double? VolumeScale { get; set; }

	[JsonProperty("amountScale")]
	public double? AmountScale { get; set; }
}