namespace StockSharp.Okex.Native.Model;

static class AccountLevel
{
	public const string Simple = "1";
	public const string SingleCurrMargin = "2";
	public const string MultiCurrMargin = "3";
}

static class PositionMode
{
	public const string LongShort = "long_short_mode";
	public const string Net = "net_mode";
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class AccountConfig
{
	[JsonProperty("uid")]
	public string Uid { get; set; }

	[JsonProperty("acctLv")]
	public string AccountLevel { get; set; }

	[JsonProperty("posMode")]
	public string PosMode { get; set; }

	[JsonProperty("autoLoan")]
	public bool AutoLoan { get; set; }

	[JsonProperty("greeksType")]
	public string GreeksType { get; set; }

	[JsonProperty("level")]
	public string Level { get; set; }

	[JsonProperty("levelTmp")]
	public string LevelTmp { get; set; }
}
