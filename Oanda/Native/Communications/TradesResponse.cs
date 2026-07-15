namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class TradesResponse : BaseResponse
{
	[JsonProperty("trades")]
	public IEnumerable<TradeData> Trades { get; set; }
}