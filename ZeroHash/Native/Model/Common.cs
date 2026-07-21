namespace StockSharp.ZeroHash.Native.Model;

sealed class ZeroHashApiError
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("grpc_code")]
	public int? GrpcCode { get; set; }

	[JsonProperty("http_code")]
	public int? HttpCode { get; set; }

	[JsonProperty("http_status")]
	public string HttpStatus { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	public string GetMessage()
	{
		var code = HttpCode ?? GrpcCode ?? Code;
		var prefix = !HttpStatus.IsEmpty()
			? HttpStatus
			: code?.ToString(CultureInfo.InvariantCulture);
		return prefix.IsEmpty()
			? Message
			: Message.IsEmpty() ? prefix : prefix + ": " + Message;
	}
}

sealed class ZeroHashEmptyResponse
{
}
