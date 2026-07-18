namespace StockSharp.CboeDataShop.Native.Model;

sealed class CboeSymbol
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("company_name")]
	public string CompanyName { get; set; }
}
