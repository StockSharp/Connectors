namespace StockSharp.Transaq.Native.Responses;

class NewsHeaderResponse : NewsBodyResponse
{
	public DateTime? TimeStamp { get; set; }
	public string Source { get; set; }
	public string Title { get; set; }
}