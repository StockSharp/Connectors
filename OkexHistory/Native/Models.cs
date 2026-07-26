namespace StockSharp.OkexHistory.Native;

using System.Runtime.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

enum OkxHistoryModules
{
	Trades = 1,
	Candles = 2,
	FundingRates = 3,
	OrderBook400 = 4,
	OrderBook5000 = 5,
	OrderBook50 = 6,
	BorrowingRates = 11,
}

enum OkxInstrumentTypes
{
	Spot,
	Futures,
	Swap,
	Option,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OkxBookActions
{
	Unknown,

	[EnumMember(Value = "snapshot")]
	Snapshot,

	[EnumMember(Value = "update")]
	Update,
}

class OkxResponse<T>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public T[] Data { get; set; }
}

class OkxInstrument
{
	[JsonProperty("instId")]
	public string Id { get; set; }

	[JsonProperty("tickSz")]
	public string TickSize { get; set; }

	[JsonProperty("lotSz")]
	public string LotSize { get; set; }

	[JsonProperty("listTime")]
	public string ListingTime { get; set; }

	[JsonProperty("expTime")]
	public string ExpiryTime { get; set; }

	[JsonProperty("uly")]
	public string Underlying { get; set; }
}

class OkxHistoryBatch
{
	[JsonProperty("details")]
	public OkxHistoryGroup[] Details { get; set; }
}

class OkxHistoryGroup
{
	[JsonProperty("groupDetails")]
	public OkxHistoryFile[] Files { get; set; }
}

class OkxHistoryFile
{
	[JsonProperty("filename")]
	public string FileName { get; set; }
}

class OkxOrderBook
{
	[JsonProperty("ts")]
	public string Timestamp { get; set; }

	[JsonProperty("action")]
	public OkxBookActions Action { get; set; }

	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }
}
