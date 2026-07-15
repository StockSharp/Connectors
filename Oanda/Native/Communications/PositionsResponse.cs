namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class PositionsResponse : BaseResponse
{
	[JsonProperty("positions")]
	public IEnumerable<Position> Positions { get; set; }
}