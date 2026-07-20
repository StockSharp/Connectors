namespace StockSharp.Cetus.Native.Model;

enum CetusSwapKinds
{
	ExactInput,
	ExactOutput,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CetusProviders
{
	[EnumMember(Value = "CETUS")]
	Cetus,
}

sealed class CetusToken
{
	public string CoinType { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class CetusSharedObject
{
	public string ObjectId { get; init; }
	public ulong InitialVersion { get; init; }
	public bool IsMutable { get; init; }
}

sealed class CetusMarket
{
	public string PoolId { get; init; }
	public ulong PoolInitialVersion { get; init; }
	public CetusToken CoinA { get; init; }
	public CetusToken CoinB { get; init; }
	public CetusToken BaseToken { get; init; }
	public CetusToken QuoteToken { get; init; }
	public string SecurityCode { get; set; }
}

sealed class CetusMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseCoinType { get; init; }
	public string QuoteCoinType { get; init; }
	public string SecurityCode { get; init; }
}

sealed class CetusQuote
{
	public string RequestId { get; init; }
	public CetusSwapKinds Kind { get; init; }
	public string PoolId { get; init; }
	public string InputCoinType { get; init; }
	public string OutputCoinType { get; init; }
	public ulong InputAmount { get; init; }
	public ulong OutputAmount { get; init; }
	public bool IsAToB { get; init; }
	public decimal FeeRate { get; init; }
}

sealed class CetusSwapEvent
{
	public string TransactionDigest { get; init; }
	public int EventIndex { get; init; }
	public DateTime Time { get; init; }
	public string PoolId { get; init; }
	public string PartnerId { get; init; }
	public bool IsAToB { get; init; }
	public ulong InputAmount { get; init; }
	public ulong OutputAmount { get; init; }
	public ulong ReferenceAmount { get; init; }
	public ulong FeeAmount { get; init; }
	public ulong VaultAAmount { get; init; }
	public ulong VaultBAmount { get; init; }
	public BigInteger BeforeSqrtPrice { get; init; }
	public BigInteger AfterSqrtPrice { get; init; }
	public ulong Steps { get; init; }
}

sealed class CetusPreparedTransaction
{
	public Bcs Transaction { get; init; }
	public GasCostSummary GasUsed { get; init; }
	public CetusSwapEvent Swap { get; init; }
}

sealed class CetusTransactionReceipt
{
	public string TransactionDigest { get; init; }
	public bool IsSuccessful { get; init; }
	public string Error { get; init; }
	public DateTime Time { get; init; }
	public ulong? Checkpoint { get; init; }
	public GasCostSummary GasUsed { get; init; }
	public CetusSwapEvent Swap { get; init; }
}

sealed class CetusApiException : InvalidOperationException
{
	public CetusApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
