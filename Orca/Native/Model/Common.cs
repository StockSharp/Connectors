namespace StockSharp.Orca.Native.Model;

/// <summary>Solana clusters supported by Orca Whirlpools.</summary>
public enum OrcaClusters
{
	/// <summary>Solana Mainnet Beta.</summary>
	Mainnet,
	/// <summary>Solana Devnet.</summary>
	Devnet,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OrcaCommitments
{
	[EnumMember(Value = "processed")]
	Processed,
	[EnumMember(Value = "confirmed")]
	Confirmed,
	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OrcaEncodings
{
	[EnumMember(Value = "base64")]
	Base64,
	[EnumMember(Value = "json")]
	Json,
}

sealed class OrcaToken
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public ulong Supply { get; init; }
	public string TokenProgram { get; init; }
	public string[] ExtensionTags { get; init; } = [];
	public bool IsExtensionMetadataKnown { get; init; }

	public bool IsTransferFeeEnabled => ExtensionTags.Any(static tag =>
		tag.Equals("transferFeeConfig", StringComparison.OrdinalIgnoreCase));

	public bool IsTransferHookEnabled => ExtensionTags.Any(static tag =>
		tag.Equals("transferHook", StringComparison.OrdinalIgnoreCase));
}

sealed class OrcaTick
{
	public bool IsInitialized { get; init; }
	public BigInteger LiquidityNet { get; init; }
}

sealed class OrcaTickArray
{
	public string Address { get; init; }
	public int StartTickIndex { get; init; }
	public OrcaTick[] Ticks { get; init; }
}

sealed class OrcaMarket
{
	public string PoolAddress { get; init; }
	public string WhirlpoolsConfig { get; set; }
	public ushort TickSpacing { get; set; }
	public ushort FeeTierIndexSeed { get; set; }
	public ushort FeeRate { get; set; }
	public BigInteger Liquidity { get; set; }
	public BigInteger SqrtPrice { get; set; }
	public int CurrentTickIndex { get; set; }
	public string TokenVaultA { get; set; }
	public string TokenVaultB { get; set; }
	public OrcaToken TokenA { get; set; }
	public OrcaToken TokenB { get; set; }
	public OrcaTickArray[] TickArrays { get; set; } = [];
	public string SecurityCode { get; set; }

	public bool IsAdaptiveFee => TickSpacing != FeeTierIndexSeed;

	public bool IsDirectTradingSupported => !IsAdaptiveFee &&
		(TokenA.TokenProgram.Equals(OrcaExtensions.TokenProgramAddress,
			StringComparison.Ordinal) || TokenA.IsExtensionMetadataKnown) &&
		(TokenB.TokenProgram.Equals(OrcaExtensions.TokenProgramAddress,
			StringComparison.Ordinal) || TokenB.IsExtensionMetadataKnown) &&
		!TokenA.IsTransferFeeEnabled && !TokenB.IsTransferFeeEnabled &&
		!TokenA.IsTransferHookEnabled && !TokenB.IsTransferHookEnabled;
}

sealed class OrcaMarketDefinition
{
	public string PoolAddress { get; init; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
	public OrcaApiPool ApiPool { get; init; }
}

sealed class OrcaQuote
{
	public BigInteger BaseAmount { get; init; }
	public BigInteger QuoteAmount { get; init; }
	public BigInteger QuoteLimit { get; init; }
	public bool IsAmountSpecifiedInput { get; init; }
	public bool IsAToB { get; init; }
	public string[] TickArrayAddresses { get; init; }
}

sealed class OrcaTrade
{
	public string Id { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class OrcaEvent
{
	public int EventIndex { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public bool IsAToB { get; init; }
	public BigInteger PostSqrtPrice { get; init; }
	public ulong InputAmount { get; init; }
	public ulong OutputAmount { get; init; }
	public ulong InputTransferFee { get; init; }
	public ulong OutputTransferFee { get; init; }
}

sealed class OrcaCandle
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

sealed class OrcaTransactionReceipt
{
	public string Signature { get; init; }
	public bool IsSuccessful { get; init; }
	public ulong Fee { get; init; }
	public long Slot { get; init; }
	public DateTime? BlockTime { get; init; }
	public string[] LogMessages { get; init; }
}

sealed class OrcaApiException : InvalidOperationException
{
	public OrcaApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
