namespace StockSharp.CboeDataShop.Native.Model;

sealed class CboeTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }
}

sealed class CboeErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }

	[JsonProperty("details")]
	public string Details { get; set; }

	public string GetMessage()
		=> Message.IsEmpty(ErrorDescription).IsEmpty(Details).IsEmpty(Error);
}
