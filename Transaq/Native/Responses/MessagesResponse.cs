namespace StockSharp.Transaq.Native.Responses;

class MessagesResponse : BaseResponse
{
	public IEnumerable<TransaqMessage> Messages { get; set; }
}

class TransaqMessage
{
	public DateTime? Date { get; set; }
	public bool Urgent { get; set; }
	public string From { get; set; }
	public string Text { get; set; }
}