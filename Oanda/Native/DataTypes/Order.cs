namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class ClientExtensions
{
	[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
	public string Id { get; set; }

	[JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
	public string Tag { get; set; }

	[JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
	public string Comment { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Order
{
	[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
	public long? Id { get; set; }

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public string Type { get; set; }

	[JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
	public string Side { get; set; }

	[JsonProperty("instrument", NullValueHandling = NullValueHandling.Ignore)]
	public string Instrument { get; set; }

	[JsonProperty("units", NullValueHandling = NullValueHandling.Ignore)]
	public double? Units { get; set; }

	[JsonProperty("time", NullValueHandling = NullValueHandling.Ignore)]
	public string Time { get; set; }

	[JsonProperty("createTime", NullValueHandling = NullValueHandling.Ignore)]
	public string CreateTime { get; set; }

	[JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
	public string State { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("timeInForce", NullValueHandling = NullValueHandling.Ignore)]
	public string TimeInForce { get; set; }

	[JsonProperty("gtdTime", NullValueHandling = NullValueHandling.Ignore)]
	public string GtdTime { get; set; }

	[JsonProperty("positionFill", NullValueHandling = NullValueHandling.Ignore)]
	public string PositionFill { get; set; }

	[JsonProperty("clientExtensions", NullValueHandling = NullValueHandling.Ignore)]
	public ClientExtensions ClientExtensions { get; set; }

	[JsonProperty("stopLoss", NullValueHandling = NullValueHandling.Ignore)]
	public double? StopLoss { get; set; }

	[JsonProperty("takeProfit", NullValueHandling = NullValueHandling.Ignore)]
	public double? TakeProfit { get; set; }

	[JsonProperty("expiry", NullValueHandling = NullValueHandling.Ignore)]
	public string Expiry { get; set; }

	[JsonProperty("upperBound", NullValueHandling = NullValueHandling.Ignore)]
	public double? UpperBound { get; set; }

	[JsonProperty("lowerBound", NullValueHandling = NullValueHandling.Ignore)]
	public double? LowerBound { get; set; }

	[JsonProperty("trailingStop", NullValueHandling = NullValueHandling.Ignore)]
	public double? TrailingStop { get; set; }

	[JsonProperty("triggerCondition", NullValueHandling = NullValueHandling.Ignore)]
	public string TriggerCondition { get; set; }

	[JsonProperty("userID", NullValueHandling = NullValueHandling.Ignore)]
	public int? UserId { get; set; }
}