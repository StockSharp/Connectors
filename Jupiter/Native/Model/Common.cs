namespace StockSharp.Jupiter.Native.Model;

enum JupiterMarketKinds
{
	Spot,
	Perpetual,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterSwapModes
{
	[EnumMember(Value = "ExactIn")]
	ExactInput,

	[EnumMember(Value = "ExactOut")]
	ExactOutput,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterPerpetualAssets
{
	[EnumMember(Value = "SOL")]
	Sol,

	[EnumMember(Value = "BTC")]
	Bitcoin,

	[EnumMember(Value = "ETH")]
	Ethereum,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterPerpetualSides
{
	[EnumMember(Value = "long")]
	Long,

	[EnumMember(Value = "short")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterPerpetualRequestTypes
{
	[EnumMember(Value = "tp")]
	TakeProfit,

	[EnumMember(Value = "sl")]
	StopLoss,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterPerpetualTransactionActions
{
	[EnumMember(Value = "increase-position")]
	IncreasePosition,

	[EnumMember(Value = "decrease-position")]
	DecreasePosition,

	[EnumMember(Value = "create-limit-order")]
	CreateLimitOrder,

	[EnumMember(Value = "update-limit-order")]
	UpdateLimitOrder,

	[EnumMember(Value = "cancel-limit-order")]
	CancelLimitOrder,

	[EnumMember(Value = "create-tpsl")]
	CreateTakeProfitStopLoss,

	[EnumMember(Value = "update-tpsl")]
	UpdateTakeProfitStopLoss,

	[EnumMember(Value = "cancel-tpsl")]
	CancelTakeProfitStopLoss,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterSpotTradeTypes
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterExecutionStatuses
{
	[EnumMember(Value = "Success")]
	Success,

	[EnumMember(Value = "Failed")]
	Failed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum JupiterPerpetualTradeActions
{
	[EnumMember(Value = "Increase")]
	Increase,

	[EnumMember(Value = "Decrease")]
	Decrease,
}

enum JupiterTrackedOrderKinds
{
	SpotSwap,
	PerpetualMarket,
	PerpetualLimit,
	PerpetualClose,
	TakeProfit,
	StopLoss,
}

sealed class JupiterToken
{
	[JsonProperty("id")]
	public string Mint { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("tokenProgram")]
	public string TokenProgram { get; set; }

	[JsonProperty("usdPrice")]
	public decimal? UsdPrice { get; set; }

	[JsonProperty("isVerified")]
	public bool? IsVerified { get; set; }

	[JsonProperty("stats24h")]
	public JupiterTokenStats Statistics24Hours { get; set; }

	[JsonProperty("scaledUiConfig")]
	public JupiterScaledUiConfig ScaledUiConfig { get; set; }
}

sealed class JupiterTokenStats
{
	[JsonProperty("priceChange")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("buyVolume")]
	public decimal? BuyVolume { get; set; }

	[JsonProperty("sellVolume")]
	public decimal? SellVolume { get; set; }
}

sealed class JupiterScaledUiConfig
{
	[JsonProperty("multiplier")]
	public decimal Multiplier { get; set; }

	[JsonProperty("newMultiplier")]
	public decimal NewMultiplier { get; set; }

	[JsonProperty("newMultiplierEffectiveAt")]
	public string NewMultiplierEffectiveAt { get; set; }
}

sealed class JupiterMarket
{
	public JupiterMarketKinds Kind { get; init; }
	public string SecurityCode { get; init; }
	public JupiterToken BaseToken { get; init; }
	public JupiterToken QuoteToken { get; init; }
	public JupiterPerpetualAssets? PerpetualAsset { get; init; }
	public decimal LastPrice { get; set; }
}

sealed class JupiterMarketDefinition
{
	public string BaseMint { get; init; }
	public string QuoteMint { get; init; }
	public string SecurityCode { get; init; }
}

sealed class JupiterApiException : InvalidOperationException
{
	public JupiterApiException(string message)
		: base(message)
	{
	}

	public JupiterApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
