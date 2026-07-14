namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Dividend
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("dividend_type")]
	public string DividendType { get; set; }

	[JsonProperty("cash_amount")]
	public double CashAmount { get; set; }

	[JsonProperty("currency_i_d")]
	public string CurrencyID { get; set; }

	[JsonProperty("declaration_date")]
	public DateTime? DeclarationDate { get; set; }

	[JsonProperty("ex_date")]
	public DateTime? ExDate { get; set; }

	[JsonProperty("record_date")]
	public DateTime? RecordDate { get; set; }

	[JsonProperty("pay_date")]
	public DateTime? PayDate { get; set; }

	[JsonProperty("frequency")]
	public int Frequency { get; set; }
}