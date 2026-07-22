namespace StockSharp.Amberdata.Native.Model;

sealed class AmberdataResponse<TPayload>
{
	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("payload")]
	public TPayload Payload { get; set; }
}

sealed class AmberdataPagedPayload<TItem>
{
	[JsonProperty("metadata")]
	public AmberdataPageMetadata Metadata { get; set; }

	[JsonProperty("data")]
	public TItem[] Data { get; set; }
}

sealed class AmberdataPageMetadata
{
	[JsonProperty("next")]
	public string Next { get; set; }

	[JsonProperty("api-version")]
	public string ApiVersion { get; set; }
}

sealed class AmberdataError
{
	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}

sealed class AmberdataReference
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("quoteSymbol")]
	public string QuoteSymbol { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchangeEnabled")]
	public bool IsExchangeEnabled { get; set; }

	[JsonProperty("limitsPriceMin")]
	public decimal? MinimumPrice { get; set; }

	[JsonProperty("limitsPriceMax")]
	public decimal? MaximumPrice { get; set; }

	[JsonProperty("limitsVolumeMin")]
	public decimal? MinimumVolume { get; set; }

	[JsonProperty("limitsVolumeMax")]
	public decimal? MaximumVolume { get; set; }

	[JsonProperty("precisionPrice")]
	public decimal? PricePrecision { get; set; }

	[JsonProperty("precisionVolume")]
	public decimal? VolumePrecision { get; set; }

	[JsonProperty("listingTimestamp")]
	public long? ListingTimestamp { get; set; }
}
