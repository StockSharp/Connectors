namespace StockSharp.Transaq.Native.Responses;

class PositionsResponse : BaseResponse
{
	public IEnumerable<MoneyPosition> MoneyPositions { get; set; }
	public IEnumerable<SecPosition> SecPositions { get; set; }
	public IEnumerable<FortsPosition> FortsPositions { get; set; }
	public IEnumerable<FortsMoney> FortsMoneys { get; set; }
	public IEnumerable<FortsCollaterals> FortsCollateralses { get; set; }
	public IEnumerable<SpotLimit> SpotLimits { get; set; }
}

class FortsPosition : Position
{
	public IEnumerable<Market> Markets { get; set; }
	public double? StartNet { get; set; }
	public double? OpenBuys { get; set; }
	public double? OpenSells { get; set; }
	public double? TotalNet { get; set; }
	public double? TodayBuy { get; set; }
	public double? TodaySell { get; set; }
	public double? OptMargin { get; set; }
	public double? VarMargin { get; set; }
	public double? ExpirationPos { get; set; }
	public double? UsedSellSpotLimit { get; set; }
	public double? SellSpotLimit { get; set; }
	public double? Netto { get; set; }
	public double? Kgo { get; set; }
}

class SpotLimit : Position
{
	public IEnumerable<Market> Markets { get; set; }
	public double? BuyLimit { get; set; }
	public double? BuyLimitUsed { get; set; }
}

class FortsCollaterals : Position
{
	public IEnumerable<Market> Markets { get; set; }
	public double? Current { get; set; }
	public double? Blocked { get; set; }
	public double? Free { get; set; }
}

class FortsMoney : FortsCollaterals
{
	public double? VarMargin { get; set; }
}

class SecPosition : Position
{
	public string Register { get; set; }
	public double? SaldoIn { get; set; }
	public double? SaldoMin { get; set; }
	public double? Bought { get; set; }
	public double? Sold { get; set; }
	public double? Saldo { get; set; }
	public double? OrdBuy { get; set; }
	public double? OrdSell { get; set; }
	public double? Amount { get; set; }
	public double? Equity { get; set; }
}

class MoneyPosition : Position
{
	public IEnumerable<Market> Markets { get; set; }
	public string Register { get; set; }
	public string Asset { get; set; }
	public double? SaldoIn { get; set; }
	public double? Bought { get; set; }
	public double? Sold { get; set; }
	public double? Saldo { get; set; }
	public double? OrdBuy { get; set; }
	public double? OrdBuyCond { get; set; }
	public double? Commission { get; set; }
	public string Currency { get; set; }
}

class Position
{
	public string Client { get; set; }
	public string Union { get; set; }
	public int SecId { get; set; }
	public int Market { get; set; }
	public string SecCode { get; set; }
	public string ShortName { get; set; }
}