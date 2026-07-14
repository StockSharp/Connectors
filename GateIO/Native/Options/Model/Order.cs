namespace StockSharp.GateIO.Native.Options.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("user")]
	public long User { get; set; }

	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("iceberg")]
	public double? Iceberg { get; set; }

	[JsonProperty("left")]
	public double? Left { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("is_liq")]
	public bool IsLiq { get; set; }

	[JsonProperty("is_close")]
	public bool IsClose { get; set; }

	[JsonProperty("fill_price")]
	public double? FillPrice { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("tkfr")]
	public double? Tkfr { get; set; }

	[JsonProperty("mkfr")]
	public double? Mkfr { get; set; }

	[JsonProperty("refu")]
	public long Refu { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("finish_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? FinishTime { get; set; }

	[JsonProperty("finish_as")]
	public string FinishAs { get; set; }

	[JsonProperty("tif")]
	public string Tif { get; set; }

	[JsonProperty("reduce_only")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("auto_size")]
	public string AutoSize { get; set; }

	[JsonProperty("is_reduce_only")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("is_close_order")]
	public bool IsCloseOrder { get; set; }

	[JsonProperty("mtime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime MTime { get; set; }
}