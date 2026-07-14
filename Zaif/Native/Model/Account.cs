namespace StockSharp.Zaif.Native.Model;

class AccountRights
{
	[JsonProperty("info")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool Info { get; set; }

	[JsonProperty("trade")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool Trade { get; set; }

	[JsonProperty("withdraw")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool Withdraw { get; set; }

	[JsonProperty("personal_info")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool PersonalInfo { get; set; }

	[JsonProperty("id_info")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool IdInfo { get; set; }
}

class Account
{
	[JsonProperty("funds")]
	public IDictionary<string, double> Funds { get; set; }

	[JsonProperty("deposit")]
	public IDictionary<string, double> Deposit { get; set; }

	[JsonProperty("rights")]
	public AccountRights Rights { get; set; }

	[JsonProperty("server_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}