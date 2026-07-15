namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class ErrorResponse
{
	[JsonProperty("errorCode")]
	public string Code { get; set; }

	[JsonProperty("errorMessage")]
	public string Message { get; set; }

	//[JsonProperty("moreInfo")]
	//public string MoreInfo { get; set; }
}