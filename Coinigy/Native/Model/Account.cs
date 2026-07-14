namespace StockSharp.Coinigy.Native.Model;

class Account
{
	[JsonProperty("authKey")]
	public string AuthKey { get; set; }

	[JsonProperty("authSecret")]
	public string AuthSecret { get; set; }

	[JsonProperty("authExchId")]
	public int AuthExchId { get; set; }

	[JsonProperty("authNickname")]
	public string AuthNickname { get; set; }

	[JsonProperty("authActive")]
	public bool AuthActive { get; set; }

	[JsonProperty("authTrade")]
	public bool AuthTrade { get; set; }

	[JsonProperty("authOptional1")]
	public string AuthOptional1 { get; set; }

	[JsonProperty("authId")]
	public int AuthId { get; set; }

	[JsonProperty("authUserId")]
	public int AuthUserId { get; set; }

	[JsonProperty("authAdded")]
	public DateTime AuthAdded { get; set; }

	[JsonProperty("authUpdated")]
	public DateTime AuthUpdated { get; set; }

	[JsonProperty("authTradingType")]
	public string AuthTradingType { get; set; }

	[JsonProperty("authHasSecondary")]
	public bool AuthHasSecondary { get; set; }

	[JsonProperty("authVersion")]
	public int AuthVersion { get; set; }
}