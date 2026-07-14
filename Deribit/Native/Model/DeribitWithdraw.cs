namespace StockSharp.Deribit.Native.Model;

class DeribitWithdraw
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("confirmed_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? ConfirmedTimestamp { get; set; }

	[JsonProperty("created_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Created { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("priority")]
	public int Priority { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("transaction_id")]
	public string TransactionId { get; set; }

	[JsonProperty("updated_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Updated { get; set; }
}