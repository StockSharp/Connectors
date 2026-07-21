namespace StockSharp.ZeroHash.Native.Model;

sealed class ZeroHashListInstrumentsRequest
{
	[JsonProperty("page_size")]
	public int PageSize { get; set; }

	[JsonProperty("page_token", NullValueHandling = NullValueHandling.Ignore)]
	public string PageToken { get; set; }

	[JsonProperty("symbols", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Symbols { get; set; }
}

sealed class ZeroHashInstrumentPage
{
	[JsonProperty("instruments")]
	public ZeroHashInstrument[] Instruments { get; set; }

	[JsonProperty("next_page_token")]
	public string NextPageToken { get; set; }

	[JsonProperty("eof")]
	public bool IsEndOfFile { get; set; }
}

sealed class ZeroHashInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("tick_size")]
	public decimal? TickSize { get; set; }

	[JsonProperty("minimum_trade_qty")]
	public string MinimumTradeQuantity { get; set; }

	[JsonProperty("fractional_qty_scale")]
	public string FractionalQuantityScale { get; set; }

	[JsonProperty("price_scale")]
	public string PriceScale { get; set; }

	[JsonProperty("non_tradable")]
	public bool IsNonTradable { get; set; }

	[JsonProperty("state")]
	public ZeroHashInstrumentStates? State { get; set; }

	[JsonProperty("forex_attributes")]
	public ZeroHashForexAttributes ForexAttributes { get; set; }
}

sealed class ZeroHashForexAttributes
{
	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }
}
