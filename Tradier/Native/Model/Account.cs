namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Account
{
	[JsonProperty("account_number")]
	public string Id { get; set; }

	[JsonProperty("day_trader")]
	public bool DayTrader { get; set; }

	[JsonProperty("option_level")]
	public int OptionLevel { get; set; }

	[JsonProperty("status")]
	public TradierPortfolioStatuses Status { get; set; }

	[JsonProperty("type")]
	public TradierAccountTypes Type { get; set; }

	[JsonProperty("date_created")]
	public DateTime DateCreated { get; set; }

	[JsonProperty("last_update_date")]
	public DateTime LastUpdateDate { get; set; }

	[JsonProperty("classification")]
	public string Classification { get; set; }
}
