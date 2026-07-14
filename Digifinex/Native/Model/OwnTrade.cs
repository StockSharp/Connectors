namespace StockSharp.Digifinex.Native.Model;

using System;
using System.Reflection;

using Ecng.Serialization;

using Newtonsoft.Json;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OwnTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("is_maker")]
	public bool? IsMaker { get; set; }
}