namespace StockSharp.Transaq.Native.Responses;

class ClientLimitsResponse : BaseResponse
{
	public string Client { get; set; }
	public double? CBPLimit { get; set; }
	public double? CBPlused { get; set; }
	public double? CBPLPlanned { get; set; }
	public double? FobVarMargin { get; set; }
	public double? Coverage { get; set; }
	public double? LiquidityC { get; set; }
	public double? Profit { get; set; }
	public double? MoneyCurrent { get; set; }
	public double? MoneyBlocked { get; set; }
	public double? MoneyFree { get; set; }
	public double? OptionsPremium { get; set; }
	public double? ExchangeFee { get; set; }
	public double? FortsVarMargin { get; set; }
	public double? VarMargin { get; set; }
	public double? PclMargin { get; set; }
	public double? OptionsVm { get; set; }
	public double? SpotBuyLimit { get; set; }
	public double? UsedStopBuyLimit { get; set; }
	public double? CollatCurrent { get; set; }
	public double? CollatBlocked { get; set; }
	public double? CollatFree { get; set; }
}