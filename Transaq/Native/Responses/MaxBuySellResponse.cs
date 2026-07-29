namespace StockSharp.Transaq.Native.Responses;

class MaxBuySellResponse : BaseResponse
{
	public string Client { get; set; }
	public string Union { get; set; }

	public IEnumerable<MaxBuySellSecurity> Securities { get; set; }
}

class MaxBuySellSecurity
{
	public string SecId { get; set; }
	public int Market { get; set; }
	public string SecCode { get; set; }

	public long MaxBuy { get; set; }

	public long MaxSell { get; set; }
}