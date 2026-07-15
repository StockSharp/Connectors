namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class AccountDetails
{
	[JsonProperty("account")]
	public Account Account { get; set; }

	[JsonProperty("lastTransactionID")]
	public long? LastTransactionId { get; set; }
}