namespace StockSharp.Meteora.Native.Model;

/// <summary>Solana clusters supported by Meteora DLMM.</summary>
public enum MeteoraClusters
{
	/// <summary>Solana Mainnet Beta.</summary>
	Mainnet,
	/// <summary>Solana Devnet.</summary>
	Devnet,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MeteoraCommitments
{
	[EnumMember(Value = "processed")]
	Processed,
	[EnumMember(Value = "confirmed")]
	Confirmed,
	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MeteoraEncodings
{
	[EnumMember(Value = "base64")]
	Base64,
	[EnumMember(Value = "json")]
	Json,
}

enum MeteoraPairTypes
{
	Permissionless,
	Permission,
	CustomizablePermissionless,
	PermissionlessV2,
}

enum MeteoraPairStates
{
	Enabled,
	Disabled,
}

enum MeteoraFunctionTypes
{
	Undetermined,
	LiquidityMining,
	LimitOrder,
}

enum MeteoraCollectFeeModes
{
	InputOnly,
	OnlyY,
}

sealed class MeteoraToken
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public ulong Supply { get; init; }
	public string TokenProgram { get; init; }
	public int AccountLength { get; init; }

	public bool IsToken2022Extended =>
		TokenProgram.Equals(MeteoraExtensions.Token2022ProgramAddress,
			StringComparison.Ordinal) && AccountLength > 82;
}

sealed class MeteoraStaticParameters
{
	public ushort BaseFactor { get; init; }
	public ushort FilterPeriod { get; init; }
	public ushort DecayPeriod { get; init; }
	public ushort ReductionFactor { get; init; }
	public uint VariableFeeControl { get; init; }
	public uint MaximumVolatilityAccumulator { get; init; }
	public int MinimumBinId { get; init; }
	public int MaximumBinId { get; init; }
	public ushort ProtocolShare { get; init; }
	public byte BaseFeePowerFactor { get; init; }
	public MeteoraFunctionTypes FunctionType { get; init; }
	public MeteoraCollectFeeModes CollectFeeMode { get; init; }
}

sealed class MeteoraVariableParameters
{
	public uint VolatilityAccumulator { get; set; }
	public uint VolatilityReference { get; set; }
	public int IndexReference { get; set; }
	public long LastUpdateTimestamp { get; set; }

	public MeteoraVariableParameters Clone()
		=> new()
		{
			VolatilityAccumulator = VolatilityAccumulator,
			VolatilityReference = VolatilityReference,
			IndexReference = IndexReference,
			LastUpdateTimestamp = LastUpdateTimestamp,
		};
}

sealed class MeteoraBin
{
	public int Id { get; init; }
	public ulong AmountX { get; init; }
	public ulong AmountY { get; init; }
	public BigInteger Price { get; init; }
	public BigInteger LiquiditySupply { get; init; }
	public ulong OpenOrderAmount { get; init; }
	public ulong ProcessedOrderRemainingAmount { get; init; }
	public bool IsLimitOrderAskSide { get; init; }
}

sealed class MeteoraBinArray
{
	public string Address { get; init; }
	public long Index { get; init; }
	public byte Version { get; init; }
	public MeteoraBin[] Bins { get; init; } = [];
}

sealed class MeteoraMarket
{
	public string PoolAddress { get; init; }
	public MeteoraStaticParameters Parameters { get; set; }
	public MeteoraVariableParameters VariableParameters { get; set; }
	public MeteoraPairTypes PairType { get; set; }
	public int ActiveId { get; set; }
	public ushort BinStep { get; set; }
	public MeteoraPairStates State { get; set; }
	public string TokenVaultX { get; set; }
	public string TokenVaultY { get; set; }
	public string Oracle { get; set; }
	public string BitmapExtension { get; set; }
	public bool IsBitmapExtensionInitialized { get; set; }
	public string[] RewardMints { get; set; } = [];
	public MeteoraToken TokenX { get; set; }
	public MeteoraToken TokenY { get; set; }
	public MeteoraBinArray[] BinArrays { get; set; } = [];
	public string SecurityCode { get; set; }
	public decimal CurrentPrice { get; set; }
	public decimal TotalValueLocked { get; set; }
	public decimal OneDayVolume { get; set; }
	public decimal OneDayFees { get; set; }
	public decimal DynamicFeePercent { get; set; }

	public bool IsSwapEnabled => State == MeteoraPairStates.Enabled;

	public bool IsLimitOrderPool =>
		Parameters.FunctionType == MeteoraFunctionTypes.LimitOrder ||
		Parameters.FunctionType == MeteoraFunctionTypes.Undetermined &&
		RewardMints.All(static mint =>
			mint.Equals(MeteoraExtensions.SystemProgramAddress,
				StringComparison.Ordinal));

	public bool IsDirectTradingSupported => IsSwapEnabled &&
		!TokenX.IsToken2022Extended && !TokenY.IsToken2022Extended;
}

sealed class MeteoraMarketDefinition
{
	public string PoolAddress { get; init; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
	public MeteoraApiPool ApiPool { get; init; }
}

sealed class MeteoraQuote
{
	public BigInteger BaseAmount { get; init; }
	public BigInteger QuoteAmount { get; init; }
	public BigInteger OtherAmountThreshold { get; init; }
	public bool IsExactInput { get; init; }
	public bool IsSwapForY { get; init; }
	public string[] BinArrayAddresses { get; init; } = [];
}

sealed class MeteoraTrade
{
	public string Id { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class MeteoraEvent
{
	public int EventIndex { get; init; }
	public string Signature { get; init; }
	public string PoolAddress { get; init; }
	public DateTime Time { get; init; }
	public bool IsSwapForY { get; init; }
	public int StartBinId { get; init; }
	public int EndBinId { get; init; }
	public ulong InputAmount { get; init; }
	public ulong InputAmountLeft { get; init; }
	public ulong OutputAmount { get; init; }
}

sealed class MeteoraCandle
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

sealed class MeteoraTransactionReceipt
{
	public string Signature { get; init; }
	public bool IsSuccessful { get; init; }
	public ulong Fee { get; init; }
	public long Slot { get; init; }
	public DateTime? BlockTime { get; init; }
	public string[] LogMessages { get; init; }
	public MeteoraRpcInnerInstructionGroup[] InnerInstructions { get; init; } = [];
}

sealed class MeteoraInstructionPlan
{
	public TransactionInstruction[] Instructions { get; init; } = [];
	public Account AdditionalSigner { get; init; }
	public string OrderAddress { get; init; }
	public int? BinId { get; init; }
}

sealed class MeteoraApiException : InvalidOperationException
{
	public MeteoraApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
