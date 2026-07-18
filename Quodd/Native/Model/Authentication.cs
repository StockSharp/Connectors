namespace StockSharp.Quodd.Native.Model;

[DataContract]
sealed class QuoddTokenResponse
{
	[DataMember(Name = "token")]
	public string Token { get; set; }
}

[DataContract]
sealed class QuoddErrorResponse
{
	[DataMember(Name = "error")]
	public string Error { get; set; }

	[DataMember(Name = "message")]
	public string Message { get; set; }

	[DataMember(Name = "detail")]
	public string Detail { get; set; }

	public string GetMessage()
		=> Message.IsEmpty(Error).IsEmpty(Detail);
}
