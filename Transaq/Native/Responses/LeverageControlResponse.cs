namespace StockSharp.Transaq.Native.Responses;

class LeverageControlResponse : BaseResponse
{
	public string Client { get; set; }
	public double? LeveragePlan { get; set; }
	public double? LeverageFact { get; set; }

	public IEnumerable<LeverageControlSecurity> Items { get; set; }
}

class LeverageControlSecurity
{
	public string Board { get; set; }
	public string SecCode { get; set; }
	public long MaxBuy { get; set; }
	public long MaxSell { get; set; }
}