namespace StockSharp.PumpSwap.Native.Model;

enum PumpSwapTradeTypes
{
	Buy,
	Sell,
}

/// <summary>Solana clusters supported by PumpSwap.</summary>
public enum PumpSwapClusters
{
	/// <summary>Solana Mainnet Beta.</summary>
	Mainnet,
	/// <summary>Solana Devnet.</summary>
	Devnet,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PumpSwapCommitments
{
	[EnumMember(Value = "processed")]
	Processed,
	[EnumMember(Value = "confirmed")]
	Confirmed,
	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PumpSwapEncodings
{
	[EnumMember(Value = "base64")]
	Base64,
	[EnumMember(Value = "json")]
	Json,
}

sealed class PumpSwapToken
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public ulong Supply { get; init; }
	public string TokenProgram { get; init; }
}

sealed class PumpSwapMarket
{
	public string PoolAddress { get; init; }
	public int PoolDataLength { get; set; }
	public ushort Index { get; init; }
	public string Creator { get; init; }
	public string CoinCreator { get; init; }
	public string PoolBaseTokenAccount { get; init; }
	public string PoolQuoteTokenAccount { get; init; }
	public PumpSwapToken BaseToken { get; set; }
	public PumpSwapToken QuoteToken { get; set; }
	public bool IsMayhemMode { get; init; }
	public bool IsCashbackCoin { get; init; }
	public BigInteger VirtualQuoteReserves { get; set; }
	public ulong BaseReserves { get; set; }
	public ulong QuoteReserves { get; set; }
	public string SecurityCode { get; set; }
}

sealed class PumpSwapMarketDefinition
{
	public string PoolAddress { get; init; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
}

readonly record struct PumpSwapFees(BigInteger LpBasisPoints,
	BigInteger ProtocolBasisPoints, BigInteger CreatorBasisPoints);

readonly record struct PumpSwapFeeTier(BigInteger MarketCapThreshold,
	PumpSwapFees Fees);

sealed class PumpSwapGlobalConfig
{
	public BigInteger LpFeeBasisPoints { get; init; }
	public BigInteger ProtocolFeeBasisPoints { get; init; }
	public string[] ProtocolFeeRecipients { get; init; }
	public BigInteger CoinCreatorFeeBasisPoints { get; init; }
	public string ReservedFeeRecipient { get; init; }
	public string[] ReservedFeeRecipients { get; init; }
	public string[] BuybackFeeRecipients { get; init; }
}

sealed class PumpSwapFeeConfig
{
	public PumpSwapFees FlatFees { get; init; }
	public PumpSwapFeeTier[] FeeTiers { get; init; }
}

sealed class PumpSwapQuote
{
	public BigInteger BaseAmount { get; init; }
	public BigInteger QuoteAmount { get; init; }
	public BigInteger QuoteLimit { get; init; }
}

sealed class PumpSwapTrade
{
	public string Id { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public ulong PoolBaseReserves { get; init; }
	public ulong PoolQuoteReserves { get; init; }
}

sealed class PumpSwapEvent
{
	public int EventIndex { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public PumpSwapTradeTypes TradeType { get; init; }
	public ulong BaseAmount { get; init; }
	public ulong QuoteAmount { get; init; }
	public ulong PoolBaseReserves { get; init; }
	public ulong PoolQuoteReserves { get; init; }
}

sealed class PumpSwapCandle
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

sealed class PumpSwapTransactionReceipt
{
	public string Signature { get; init; }
	public bool IsSuccessful { get; init; }
	public ulong Fee { get; init; }
	public long Slot { get; init; }
	public DateTime? BlockTime { get; init; }
	public string[] LogMessages { get; init; }
}

sealed class PumpSwapApiException : InvalidOperationException
{
	public PumpSwapApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
