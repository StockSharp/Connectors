namespace StockSharp.Bluefin.Native.Model;

sealed class BluefinApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }
}

sealed class BluefinExchangeInfo
{
	[JsonProperty("assets")]
	public BluefinAssetConfig[] Assets { get; init; }

	[JsonProperty("contractsConfig")]
	public BluefinContractsConfig ContractsConfig { get; init; }

	[JsonProperty("markets")]
	public BluefinMarket[] Markets { get; init; }

	[JsonProperty("tradingGasFeeE9")]
	public string TradingGasFeeE9 { get; init; }

	[JsonProperty("serverTimeAtMillis")]
	public long ServerTimeAtMillis { get; init; }

	[JsonProperty("timezone")]
	public string Timezone { get; init; }
}

sealed class BluefinContractsConfig
{
	[JsonProperty("edsId")]
	public string EdsId { get; init; }

	[JsonProperty("idsId")]
	public string IdsId { get; init; }

	[JsonProperty("network")]
	public string Network { get; init; }

	[JsonProperty("baseContractAddress")]
	public string BaseContractAddress { get; init; }

	[JsonProperty("currentContractAddress")]
	public string CurrentContractAddress { get; init; }
}

sealed class BluefinAssetConfig
{
	[JsonProperty("assetType")]
	public string AssetType { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("weight")]
	public string Weight { get; init; }

	[JsonProperty("marginAvailable")]
	public bool IsMarginAvailable { get; init; }
}

sealed class BluefinMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marketAddress")]
	public string MarketAddress { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("baseAssetSymbol")]
	public string BaseAssetSymbol { get; init; }

	[JsonProperty("baseAssetName")]
	public string BaseAssetName { get; init; }

	[JsonProperty("baseAssetDecimals")]
	public int BaseAssetDecimals { get; init; }

	[JsonProperty("stepSizeE9")]
	public string StepSizeE9 { get; init; }

	[JsonProperty("tickSizeE9")]
	public string TickSizeE9 { get; init; }

	[JsonProperty("minOrderQuantityE9")]
	public string MinimumOrderQuantityE9 { get; init; }

	[JsonProperty("maxLimitOrderQuantityE9")]
	public string MaximumLimitOrderQuantityE9 { get; init; }

	[JsonProperty("maxMarketOrderQuantityE9")]
	public string MaximumMarketOrderQuantityE9 { get; init; }

	[JsonProperty("minOrderPriceE9")]
	public string MinimumOrderPriceE9 { get; init; }

	[JsonProperty("maxOrderPriceE9")]
	public string MaximumOrderPriceE9 { get; init; }

	[JsonProperty("maintenanceMarginRatioE9")]
	public string MaintenanceMarginRatioE9 { get; init; }

	[JsonProperty("initialMarginRatioE9")]
	public string InitialMarginRatioE9 { get; init; }

	[JsonProperty("defaultLeverageE9")]
	public string DefaultLeverageE9 { get; init; }

	[JsonProperty("defaultMakerFeeE9")]
	public string DefaultMakerFeeE9 { get; init; }

	[JsonProperty("defaultTakerFeeE9")]
	public string DefaultTakerFeeE9 { get; init; }

	[JsonProperty("tradingStartTimeAtMillis")]
	public string TradingStartTimeAtMillis { get; init; }

	[JsonProperty("isolatedOnly")]
	public bool IsIsolatedOnly { get; init; }
}

sealed class BluefinLoginRequest
{
	[JsonProperty("accountAddress", Order = 0)]
	public string AccountAddress { get; init; }

	[JsonProperty("signedAtMillis", Order = 1)]
	public long SignedAtMillis { get; init; }

	[JsonProperty("audience", Order = 2)]
	public string Audience { get; init; }
}

sealed class BluefinLoginResponse
{
	[JsonProperty("accessToken")]
	public string AccessToken { get; init; }

	[JsonProperty("accessTokenValidForSeconds")]
	public long AccessTokenValidForSeconds { get; init; }

	[JsonProperty("refreshToken")]
	public string RefreshToken { get; init; }

	[JsonProperty("refreshTokenValidForSeconds")]
	public long RefreshTokenValidForSeconds { get; init; }
}
