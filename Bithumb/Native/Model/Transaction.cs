namespace StockSharp.Bithumb.Native.Model;

class Transaction
{
	[JsonProperty("cont_no")]
	public long Id { get; set; }

	[JsonProperty("transaction_date")]
	//[JsonConverter(typeof(JsonDateTimeConverter))]
	public string Time { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("units_traded")]
	public double Amount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}