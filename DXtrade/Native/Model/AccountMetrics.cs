namespace StockSharp.DXtrade.Native.Model;

class AccountMetrics
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("equity")]
	public double Equity { get; set; }

	[JsonProperty("balance")]
	public double Balance { get; set; }

	[JsonProperty("availableBalance")]
	public double AvailableBalance { get; set; }

	[JsonProperty("availableFunds")]
	public double AvailableFunds { get; set; }

	[JsonProperty("allocatedFunds")]
	public double AllocatedFunds { get; set; }

	[JsonProperty("marginFree")]
	public double MarginFree { get; set; }

	[JsonProperty("openPl")]
	public double OpenPl { get; set; }

	[JsonProperty("totalPl")]
	public double TotalPl { get; set; }

	[JsonProperty("margin")]
	public double Margin { get; set; }

	[JsonProperty("openPositionsCount")]
	public int OpenPositionsCount { get; set; }

	[JsonProperty("openOrdersCount")]
	public int OpenOrdersCount { get; set; }

	[JsonProperty("positions")]
	public PositionMetrics[] Positions { get; set; }

	[JsonProperty("balances")]
	public CurrencyMetrics[] Balances { get; set; }
}

class PositionMetrics
{
	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("positionCode")]
	public string PositionCode { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("quantityNotional")]
	public double QuantityNotional { get; set; }

	[JsonProperty("rate")]
	public double Rate { get; set; }

	[JsonProperty("fpl")]
	public double Fpl { get; set; }

	[JsonProperty("convRate")]
	public double ConvRate { get; set; }

	[JsonProperty("bidOpen")]
	public double BidOpen { get; set; }

	[JsonProperty("askOpen")]
	public double AskOpen { get; set; }

	[JsonProperty("margin")]
	public double Margin { get; set; }

	[JsonProperty("openPrice")]
	public double OpenPrice { get; set; }

	[JsonProperty("openPriceInstrumentCcy")]
	public double OpenPriceInstrumentCcy { get; set; }

	[JsonProperty("fplInstrumentCcy")]
	public double FplInstrumentCcy { get; set; }

	[JsonProperty("instrumentCurrency")]
	public string InstrumentCurrency { get; set; }

	[JsonProperty("currentPriceInstrumentCcy")]
	public double? CurrentPriceInstrumentCcy { get; set; }
}

class CurrencyMetrics
{
	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("balance")]
	public double Balance { get; set; }

	[JsonProperty("allocatedFunds")]
	public double AllocatedFunds { get; set; }

	[JsonProperty("availableFunds")]
	public double AvailableFunds { get; set; }

	[JsonProperty("withdrawableBalance")]
	public double WithdrawableBalance { get; set; }

	[JsonProperty("convRate")]
	public double ConvRate { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }
}