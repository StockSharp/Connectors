namespace StockSharp.PolygonIO.Native.Model;

class Trade
{
	[JsonProperty("exchange")]
	public int Exchange { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("participant_timestamp")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime? ParticipantTimestamp { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("sequence_number")]
	public long SequenceNumber { get; set; }

	[JsonProperty("sip_timestamp")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime SipTimestamp { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("tape")]
	public int? Tape { get; set; }
}