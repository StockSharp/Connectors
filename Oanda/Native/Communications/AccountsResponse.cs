namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class AccountsResponse
{
	[JsonProperty("accounts")]
	public IEnumerable<Account> Accounts { get; set; }
}