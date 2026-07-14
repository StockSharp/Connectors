namespace StockSharp.Kucoin.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Level2Entry
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class Level2
{
	[JsonProperty("sequence")]
	public long Sequence { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("bids")]
	public Level2Entry[] Bids { get; set; }

	[JsonProperty("asks")]
	public Level2Entry[] Asks { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class SocketLevel2Entry
{
	public double Price { get; set; }
	public double Size { get; set; }
	public long Sequence { get; set; }
}

class SocketLevel2Changes
{
	[JsonProperty("bids")]
	public SocketLevel2Entry[] Bids { get; set; }

	[JsonProperty("asks")]
	public SocketLevel2Entry[] Asks { get; set; }
}

class SocketLevel2
{
	[JsonProperty("sequenceStart")]
	public long SequenceStart { get; set; }

	[JsonProperty("sequenceEnd")]
	public long SequenceEnd { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("changes")]
	public SocketLevel2Changes Changes { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

class SocketLevel2Snapshot
{
	[JsonProperty("bids")]
	public Level2Entry[] Bids { get; set; }

	[JsonProperty("asks")]
	public Level2Entry[] Asks { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}