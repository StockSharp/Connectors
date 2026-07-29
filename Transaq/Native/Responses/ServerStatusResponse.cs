namespace StockSharp.Transaq.Native.Responses;

class ServerStatusResponse : BaseResponse
{
	public string Connected { get; set; }
	public string Recover { get; set; }
	public string TimeZone { get; set; }
}