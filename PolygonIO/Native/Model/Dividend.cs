namespace StockSharp.PolygonIO.Native.Model;

class Dividend
{
	[JsonProperty("cash_amount")]
	public double CashAmount { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("declaration_date")]
	public string DeclarationDate { get; set; }

	[JsonProperty("dividend_type")]
	public string DividendType { get; set; }

	[JsonProperty("ex_dividend_date")]
	public string ExDividendDate { get; set; }

	[JsonProperty("frequency")]
	public int Frequency { get; set; }

	[JsonProperty("pay_date")]
	public string PayDate { get; set; }

	[JsonProperty("record_date")]
	public string RecordDate { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }
}