namespace StockSharp.Zaif.Native.Model;

class Symbol
{
	[JsonProperty("is_token")]
	public bool IsToken { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("item_unit_step")]
	public double ItemUnitStep { get; set; }

	[JsonProperty("aux_unit_point")]
	public int AuxUnitPoint { get; set; }

	[JsonProperty("item_japanese")]
	public string ItemJapanese { get; set; }

	[JsonProperty("event_number")]
	public int EventNumber { get; set; }

	[JsonProperty("aux_japanese")]
	public string AuxJapanese { get; set; }

	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("seq")]
	public int Seq { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("aux_unit_step")]
	public double AuxUnitStep { get; set; }

	[JsonProperty("item_unit_min")]
	public double ItemUnitMin { get; set; }

	[JsonProperty("aux_unit_min")]
	public double AuxUnitMin { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("id")]
	public int Id { get; set; }
}