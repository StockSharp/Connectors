namespace StockSharp.LBank.Native.Model;

class Ticker
{
	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("latest")]
	public double? Latest { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("turnover")]
	public double? Turnover { get; set; }

	[JsonProperty("vol")]
	public double Vol { get; set; }
}

class SocketTicker
{
	[JsonProperty("to_cny")]
	public double? ToCny { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("vol")]
	public double Vol { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("usd")]
	public double? Usd { get; set; }

	[JsonProperty("to_usd")]
	public double? ToUsd { get; set; }

	[JsonProperty("dir")]
	public string Dir { get; set; }

	[JsonProperty("turnover")]
	public double? Turnover { get; set; }

	[JsonProperty("latest")]
	public double? Latest { get; set; }

	[JsonProperty("cny")]
	public double? Cny { get; set; }
}