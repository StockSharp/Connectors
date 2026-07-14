namespace StockSharp.MoexISS.Native;

[JsonConverter(typeof(JsonIssResponseToObjectConverter))]
class IssResponsePayload
{
	/// <summary>
	/// Описание колонок
	/// </summary>
	[JsonProperty("columns")]
	public List<string> Columns { get; set; }

	/// <summary>
	/// Данные
	/// </summary>
	[JsonProperty("data")]
	public List<Dictionary<string, string>> Data { get; set; }
}