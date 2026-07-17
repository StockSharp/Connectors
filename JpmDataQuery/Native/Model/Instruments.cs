namespace StockSharp.JpmDataQuery.Native.Model;

sealed class JpmDataQueryInstrumentsResponse : JpmDataQueryPage
{
	[JsonProperty("instruments")]
	public JpmDataQueryInstrument[] Instruments { get; set; }
}

sealed class JpmDataQueryInstrument
{
	[JsonProperty("item")]
	public long? Item { get; set; }

	[JsonProperty("instrument-id")]
	public string InstrumentId { get; set; }

	[JsonProperty("instrument-name")]
	public string InstrumentName { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("instrument-cusip")]
	public string Cusip { get; set; }

	[JsonProperty("instrument-isin")]
	public string Isin { get; set; }
}

sealed class JpmDataQueryInstrumentsQuery
{
	public string GroupId { get; init; }
	public string InstrumentId { get; init; }
	public string Keywords { get; init; }

	public string ToQueryString()
	{
		var query = $"group-id={Encode(GroupId)}";
		if (!InstrumentId.IsEmpty())
			query += $"&instrument-id={Encode(InstrumentId)}";
		if (!Keywords.IsEmpty())
			query += $"&keywords={Encode(Keywords)}";
		return query;
	}

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);
}
