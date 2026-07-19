namespace StockSharp.ManifestTrade.Native.Model;

/// <summary>Solana clusters supported by Manifest Trade.</summary>
public enum ManifestTradeClusters
{
	/// <summary>Solana Mainnet Beta.</summary>
	Mainnet,
	/// <summary>Solana Devnet.</summary>
	Devnet,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ManifestTradeCommitments
{
	[EnumMember(Value = "processed")]
	Processed,
	[EnumMember(Value = "confirmed")]
	Confirmed,
	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ManifestTradeEncodings
{
	[EnumMember(Value = "base64")]
	Base64,
	[EnumMember(Value = "json")]
	Json,
}

enum ManifestTradeOrderTypes : byte
{
	Limit = 0,
	ImmediateOrCancel = 1,
	PostOnly = 2,
	Global = 3,
	Reverse = 4,
	ReverseTight = 5,
}

sealed class ManifestTradeToken
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public ulong Supply { get; init; }
	public string TokenProgram { get; init; }
	public bool IsDirectTradingSupported { get; init; }
}

sealed class ManifestTradeOrder
{
	public uint Index { get; init; }
	public BigInteger RawPrice { get; init; }
	public ulong BaseAtoms { get; init; }
	public ulong Sequence { get; init; }
	public uint TraderIndex { get; init; }
	public uint LastValidSlot { get; init; }
	public bool IsBid { get; init; }
	public ManifestTradeOrderTypes OrderType { get; init; }
	public ushort ReverseSpread { get; init; }
}

sealed class ManifestTradeSeat
{
	public uint Index { get; init; }
	public string Trader { get; init; }
	public ulong BaseWithdrawableAtoms { get; init; }
	public ulong QuoteWithdrawableAtoms { get; init; }
	public ulong QuoteVolumeAtoms { get; init; }
}

sealed class ManifestTradeMarket
{
	public string MarketAddress { get; init; }
	public byte Version { get; set; }
	public ManifestTradeToken BaseToken { get; set; }
	public ManifestTradeToken QuoteToken { get; set; }
	public string BaseVault { get; set; }
	public string QuoteVault { get; set; }
	public ulong NextOrderSequence { get; set; }
	public ulong QuoteVolumeAtoms { get; set; }
	public long Slot { get; set; }
	public ManifestTradeOrder[] Bids { get; set; } = [];
	public ManifestTradeOrder[] Asks { get; set; } = [];
	public ManifestTradeSeat[] Seats { get; set; } = [];
	public string SecurityCode { get; set; }

	public bool IsDirectTradingSupported =>
		BaseToken?.IsDirectTradingSupported == true &&
		QuoteToken?.IsDirectTradingSupported == true;
}

sealed class ManifestTradeMarketDefinition
{
	public string MarketAddress { get; init; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
	public ManifestTradeTicker Ticker { get; init; }
}

sealed class ManifestTradeTrade
{
	public string Id { get; init; }
	public string Signature { get; init; }
	public string MarketAddress { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public ulong MakerSequence { get; init; }
	public ulong TakerSequence { get; init; }
	public string Maker { get; init; }
	public string Taker { get; init; }
}

sealed class ManifestTradeFillEvent
{
	public int EventIndex { get; init; }
	public string Signature { get; init; }
	public DateTime Time { get; init; }
	public string MarketAddress { get; init; }
	public string Maker { get; init; }
	public string Taker { get; init; }
	public string BaseMint { get; init; }
	public string QuoteMint { get; init; }
	public BigInteger RawPrice { get; init; }
	public ulong BaseAtoms { get; init; }
	public ulong QuoteAtoms { get; init; }
	public ulong MakerSequence { get; init; }
	public ulong TakerSequence { get; init; }
	public bool IsTakerBuy { get; init; }
}

sealed class ManifestTradePlaceEvent
{
	public string MarketAddress { get; init; }
	public string Trader { get; init; }
	public BigInteger RawPrice { get; init; }
	public ulong BaseAtoms { get; init; }
	public ulong Sequence { get; init; }
	public uint OrderIndex { get; init; }
	public ManifestTradeOrderTypes OrderType { get; init; }
	public bool IsBid { get; init; }
}

sealed class ManifestTradeCancelEvent
{
	public string MarketAddress { get; init; }
	public string Trader { get; init; }
	public ulong Sequence { get; init; }
}

sealed class ManifestTradeCandle
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

sealed class ManifestTradeTransactionReceipt
{
	public string Signature { get; init; }
	public bool IsSuccessful { get; init; }
	public ulong Fee { get; init; }
	public long Slot { get; init; }
	public DateTime? BlockTime { get; init; }
	public string[] LogMessages { get; init; }
}

sealed class ManifestTradeRpcException : InvalidOperationException
{
	public ManifestTradeRpcException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}

readonly record struct ManifestTradeBookLevel(decimal Price, decimal Volume);

sealed class ManifestTradeQuote
{
	public ulong BaseAtoms { get; init; }
	public ulong QuoteAtoms { get; init; }
	public ulong InputLimitAtoms { get; init; }
	public ulong OutputLimitAtoms { get; init; }
}
