namespace StockSharp.Transaq.Native.Commands;

class RequestMcPortfolioMessage : BaseCommandMessage
{
	public RequestMcPortfolioMessage() : base(ApiCommands.GetMcPortfolio)
	{
	}

	public string Client { get; set; }

	public string Union { get; set; }

	public bool? Currency { get; set; }

	public bool? Asset { get; set; }

	public bool? Money { get; set; }

	public bool? Depo { get; set; }

	public bool? Registers { get; set; }

	public bool? MaxBs { get; set; }
}
