namespace StockSharp.FinancialModelingPrep.Native.Model;

sealed class FmpErrorResponse
{
	[JsonProperty("Error Message")]
	public string ErrorMessage { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}
