namespace StockSharp.Upbit.Native.Model;

class Symbol
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("korean_name")]
	public string KoreanName { get; set; }

	[JsonProperty("english_name")]
	public string EnglishName { get; set; }
}