namespace StockSharp.Balancer.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BalancerGraphChains
{
	[EnumMember(Value = "MAINNET")]
	Mainnet,

	[EnumMember(Value = "ARBITRUM")]
	Arbitrum,

	[EnumMember(Value = "BASE")]
	Base,

	[EnumMember(Value = "OPTIMISM")]
	Optimism,

	[EnumMember(Value = "POLYGON")]
	Polygon,

	[EnumMember(Value = "GNOSIS")]
	Gnosis,

	[EnumMember(Value = "AVALANCHE")]
	Avalanche,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BalancerEventTypes
{
	[EnumMember(Value = "SWAP")]
	Swap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BalancerPoolTypes
{
	[EnumMember(Value = "WEIGHTED")]
	Weighted,

	[EnumMember(Value = "STABLE")]
	Stable,

	[EnumMember(Value = "META_STABLE")]
	MetaStable,

	[EnumMember(Value = "PHANTOM_STABLE")]
	PhantomStable,

	[EnumMember(Value = "COMPOSABLE_STABLE")]
	ComposableStable,

	[EnumMember(Value = "ELEMENT")]
	Element,

	[EnumMember(Value = "LIQUIDITY_BOOTSTRAPPING")]
	LiquidityBootstrapping,

	[EnumMember(Value = "INVESTMENT")]
	Investment,

	[EnumMember(Value = "GYRO")]
	Gyro,

	[EnumMember(Value = "GYRO3")]
	Gyro3,

	[EnumMember(Value = "GYROE")]
	GyroE,

	[EnumMember(Value = "FX")]
	Fx,

	[EnumMember(Value = "QUANT_AMM_WEIGHTED")]
	QuantAmmWeighted,

	[EnumMember(Value = "RECLAMM")]
	ReClamm,

	[EnumMember(Value = "FIXED_LBP")]
	FixedLbp,

	[EnumMember(Value = "COW_AMM")]
	CowAmm,

	[EnumMember(Value = "UNKNOWN")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BalancerSwapTypes
{
	[EnumMember(Value = "EXACT_IN")]
	ExactIn,

	[EnumMember(Value = "EXACT_OUT")]
	ExactOut,
}

sealed class BalancerToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public int Index { get; init; }
}

sealed class BalancerPool
{
	public string Id { get; init; }
	public string Address { get; init; }
	public string Name { get; init; }
	public string Symbol { get; init; }
	public BalancerPoolTypes Type { get; init; }
	public int PoolVersion { get; init; }
	public int ProtocolVersion { get; init; }
	public decimal TotalLiquidity { get; init; }
	public decimal Volume24Hours { get; init; }
	public decimal Fees24Hours { get; init; }
	public decimal SwapFee { get; init; }
	public bool IsSwapEnabled { get; init; }
	public BalancerToken[] Tokens { get; init; }
}

sealed class BalancerMarket
{
	public string Key { get; init; }
	public string SecurityCode { get; init; }
	public BalancerPool Pool { get; init; }
	public BalancerToken BaseToken { get; init; }
	public BalancerToken QuoteToken { get; init; }
}

sealed class BalancerMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class BalancerQuote
{
	public BalancerSwapTypes SwapType { get; init; }
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
	public decimal InputAmountDecimal { get; init; }
	public decimal OutputAmountDecimal { get; init; }
	public decimal Price { get; init; }
}

sealed class BalancerTrade
{
	public string Id { get; init; }
	public string TransactionHash { get; init; }
	public string PoolId { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public long BlockNumber { get; init; }
	public int LogIndex { get; init; }
}

sealed class BalancerCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int TradeCount { get; init; }
}

sealed class BalancerRawSwap
{
	public string PoolId { get; init; }
	public string TokenIn { get; init; }
	public string TokenOut { get; init; }
	public BigInteger AmountIn { get; init; }
	public BigInteger AmountOut { get; init; }
	public string TransactionHash { get; init; }
	public BigInteger BlockNumber { get; init; }
	public int LogIndex { get; init; }
}

sealed class BalancerTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}
