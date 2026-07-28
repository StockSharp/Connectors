namespace StockSharp.CoinSwitch.Native.Model;

sealed class CoinSwitchFuturesInstrument
{
	[JsonIgnore]
	public string NativeSymbol { get; set; }

	[JsonProperty("symbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("min_leverage")]
	public decimal MinimumLeverage { get; set; }

	[JsonProperty("max_leverage")]
	public decimal MaximumLeverage { get; set; }

	[JsonProperty("min_base_quantity")]
	public decimal MinimumVolume { get; set; }

	[JsonProperty("base_quantity_step_size")]
	public decimal VolumeStep { get; set; }

	[JsonProperty("quantity_precision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("price_precision")]
	public int PricePrecision { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("max_market_base_quantity")]
	public decimal MaximumMarketVolume { get; set; }

	[JsonProperty("max_base_quantity")]
	public decimal MaximumVolume { get; set; }

	[JsonIgnore]
	public string SecurityCode
		=> CoinSwitchExtensions.CreateSecurityCode(
			BaseAsset,
			QuoteAsset);

	[JsonIgnore]
	public decimal PriceStep
		=> TickSize *
			(CoinSwitchExtensions.GetStep(PricePrecision) ?? 1m);
}

sealed class CoinSwitchFuturesTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("low_price_24h")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("high_price_24h")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("best_ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("best_bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("best_ask_size")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("best_bid_size")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("price_24h_pcnt")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("base_asset_volume_24h")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("quote_asset_volume_24h")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("index_price")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("mark_price")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("funding_rate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("next_funding_timestamp")]
	public long NextFundingTime { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class CoinSwitchHftLeverageFilter
{
	[JsonProperty("minLeverage")]
	public decimal Minimum { get; set; }

	[JsonProperty("maxLeverage")]
	public decimal Maximum { get; set; }

	[JsonProperty("leverageStep")]
	public decimal Step { get; set; }
}

sealed class CoinSwitchHftPriceFilter
{
	[JsonProperty("minPrice")]
	public decimal Minimum { get; set; }

	[JsonProperty("maxPrice")]
	public decimal Maximum { get; set; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; set; }
}

sealed class CoinSwitchHftLotSizeFilter
{
	[JsonProperty("maxOrderQty")]
	public decimal MaximumQuantity { get; set; }

	[JsonProperty("minOrderQty")]
	public decimal MinimumQuantity { get; set; }

	[JsonProperty("qtyStep")]
	public decimal QuantityStep { get; set; }

	[JsonProperty("minNotionalValue")]
	public decimal? MinimumNotional { get; set; }
}

sealed class CoinSwitchHftInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("settleCoin")]
	public string SettlementCoin { get; set; }

	[JsonProperty("optionsType")]
	public string OptionsType { get; set; }

	[JsonProperty("launchTime")]
	public long LaunchTime { get; set; }

	[JsonProperty("deliveryTime")]
	public long DeliveryTime { get; set; }

	[JsonProperty("leverageFilter")]
	public CoinSwitchHftLeverageFilter Leverage { get; set; }

	[JsonProperty("priceFilter")]
	public CoinSwitchHftPriceFilter PriceFilter { get; set; }

	[JsonProperty("lotSizeFilter")]
	public CoinSwitchHftLotSizeFilter LotSize { get; set; }

	[JsonIgnore]
	public OptionTypes? OptionType
		=> OptionsType?.Trim().ToLowerInvariant() switch
		{
			"call" => OptionTypes.Call,
			"put" => OptionTypes.Put,
			_ => CoinSwitchExtensions.ParseOptionType(Symbol),
		};

	[JsonIgnore]
	public decimal? Strike
		=> CoinSwitchExtensions.ParseOptionStrike(Symbol);

	[JsonIgnore]
	public decimal? PriceStep => PriceFilter?.TickSize;

	[JsonIgnore]
	public decimal? VolumeStep => LotSize?.QuantityStep;
}
