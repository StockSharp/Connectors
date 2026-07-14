namespace StockSharp.PolygonIO.Native.Model;

class Quote
{
	[JsonProperty("ask_exchange")]
	public int AskExchange { get; set; }

	[JsonProperty("ask_price")]
	public double AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public double? AskSize { get; set; }

	[JsonProperty("bid_exchange")]
	public int BidExchange { get; set; }

	[JsonProperty("bid_price")]
	public double BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public double? BidSize { get; set; }

	[JsonProperty("conditions")]
	public int[] Conditions { get; set; }

	[JsonProperty("participant_timestamp")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime? ParticipantTimestamp { get; set; }

	[JsonProperty("sequence_number")]
	public long SequenceNumber { get; set; }

	[JsonProperty("sip_timestamp")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime SipTimestamp { get; set; }

	[JsonProperty("tape")]
	public int? Tape { get; set; }
}