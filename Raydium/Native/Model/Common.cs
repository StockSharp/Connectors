namespace StockSharp.Raydium.Native.Model;

/// <summary>Solana clusters supported by Raydium.</summary>
public enum RaydiumClusters
{
	/// <summary>Solana Mainnet Beta.</summary>
	Mainnet,
	/// <summary>Solana Devnet.</summary>
	Devnet,
}

/// <summary>Automatic Raydium priority-fee levels.</summary>
public enum RaydiumPriorityFeeLevels
{
	/// <summary>Medium priority.</summary>
	Medium,
	/// <summary>High priority.</summary>
	High,
	/// <summary>Very high priority.</summary>
	VeryHigh,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RaydiumCommitments
{
	[EnumMember(Value = "processed")]
	Processed,
	[EnumMember(Value = "confirmed")]
	Confirmed,
	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RaydiumEncodings
{
	[EnumMember(Value = "base64")]
	Base64,
	[EnumMember(Value = "json")]
	Json,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RaydiumSwapTypes
{
	[EnumMember(Value = "BaseIn")]
	BaseIn,
	[EnumMember(Value = "BaseOut")]
	BaseOut,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RaydiumTransactionVersions
{
	[EnumMember(Value = "V0")]
	V0,
}

enum RaydiumSolanaMessageVersions
{
	V0 = 0,
}

sealed class RaydiumToken
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public string TokenProgram { get; init; }
}

sealed class RaydiumPool
{
	public string PoolAddress { get; init; }
	public string ProgramAddress { get; init; }
	public string VaultA { get; init; }
	public string VaultB { get; init; }
	public RaydiumToken TokenA { get; init; }
	public RaydiumToken TokenB { get; init; }
	public decimal ReferencePrice { get; init; }
	public decimal TotalValueLocked { get; init; }
}

sealed class RaydiumMarket
{
	public RaydiumToken TokenA { get; init; }
	public RaydiumToken TokenB { get; init; }
	public RaydiumPool[] Pools { get; init; }
	public string SecurityCode { get; set; }
	public decimal ReferencePrice { get; init; }

	public string MintPairKey => RaydiumExtensions.GetMintPairKey(
		TokenA.Mint, TokenB.Mint);
}

sealed class RaydiumMarketDefinition
{
	public string PoolAddress { get; init; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
	public RaydiumApiPool ApiPool { get; init; }
	public RaydiumApiPoolKeys ApiKeys { get; init; }
}

sealed class RaydiumQuote
{
	public Sides Side { get; init; }
	public RaydiumMarket Market { get; init; }
	public RaydiumApiResponse<RaydiumSwapQuoteData> Response { get; init; }

	public RaydiumSwapQuoteData Data => Response.Data;
}

sealed class RaydiumTrade
{
	public string Id { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class RaydiumCandle
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

sealed class RaydiumTransactionReceipt
{
	public string Signature { get; init; }
	public bool IsSuccessful { get; init; }
	public ulong Fee { get; init; }
	public long Slot { get; init; }
	public DateTime? BlockTime { get; init; }
	public RaydiumRpcTransaction Transaction { get; init; }
}

sealed class RaydiumApiException : InvalidOperationException
{
	public RaydiumApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
