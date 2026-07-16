namespace StockSharp.Databento.Native;

internal enum DbnRecordTypes : byte
{
	Trades = 0x00,
	Mbp1 = 0x01,
	Mbp10 = 0x0A,
	OhlcvDeprecated = 0x11,
	Status = 0x12,
	InstrumentDefinition = 0x13,
	Imbalance = 0x14,
	Error = 0x15,
	SymbolMapping = 0x16,
	System = 0x17,
	Statistics = 0x18,
	Ohlcv1Second = 0x20,
	Ohlcv1Minute = 0x21,
	Ohlcv1Hour = 0x22,
	Ohlcv1Day = 0x23,
	OhlcvEndOfDay = 0x24,
	Mbo = 0xA0,
	ConsolidatedMbp1 = 0xB1,
	ConsolidatedBbo1Second = 0xC0,
	ConsolidatedBbo1Minute = 0xC1,
	ConsolidatedTbbo = 0xC2,
	Bbo1Second = 0xC3,
	Bbo1Minute = 0xC4,
}

internal enum DbnSchemas : ushort
{
	Mbo = 0,
	Mbp1 = 1,
	Mbp10 = 2,
	Tbbo = 3,
	Trades = 4,
	Ohlcv1Second = 5,
	Ohlcv1Minute = 6,
	Ohlcv1Hour = 7,
	Ohlcv1Day = 8,
	Definition = 9,
	Statistics = 10,
	Status = 11,
	Imbalance = 12,
	OhlcvEndOfDay = 13,
}

internal enum DbnSystemCodes : byte
{
	Heartbeat = 0,
	SubscriptionAcknowledged = 1,
	SlowReaderWarning = 2,
	ReplayCompleted = 3,
	EndOfInterval = 4,
	Unset = byte.MaxValue,
}

internal enum DbnStatusActions : ushort
{
	None = 0,
	PreOpen = 1,
	PreCross = 2,
	Quoting = 3,
	Cross = 4,
	Rotation = 5,
	NewPriceIndication = 6,
	Trading = 7,
	Halt = 8,
	Pause = 9,
	Suspend = 10,
	PreClose = 11,
	Close = 12,
	PostClose = 13,
	ShortSaleRestrictionChange = 14,
	NotAvailableForTrading = 15,
}

internal enum DbnStatisticTypes : ushort
{
	OpeningPrice = 1,
	IndicativeOpeningPrice = 2,
	SettlementPrice = 3,
	TradingSessionLowPrice = 4,
	TradingSessionHighPrice = 5,
	ClearedVolume = 6,
	LowestOffer = 7,
	HighestBid = 8,
	OpenInterest = 9,
	FixingPrice = 10,
	ClosePrice = 11,
	NetChange = 12,
	Vwap = 13,
	Volatility = 14,
	Delta = 15,
	UncrossingPrice = 16,
	UpperPriceLimit = 17,
	LowerPriceLimit = 18,
	BlockVolume = 19,
	IndicativeClosePrice = 20,
}

internal sealed class DbnRecordHeader
{
	public int Length { get; init; }
	public DbnRecordTypes Type { get; init; }
	public ushort PublisherId { get; init; }
	public uint InstrumentId { get; init; }
	public ulong EventTimestamp { get; init; }
}

internal abstract class DbnRecord
{
	public DbnRecordHeader Header { get; init; }
}

internal sealed class DbnUnknownRecord : DbnRecord
{
}

internal sealed class DbnBidAskPair
{
	public long BidPrice { get; init; }
	public long AskPrice { get; init; }
	public uint BidSize { get; init; }
	public uint AskSize { get; init; }
	public uint BidCount { get; init; }
	public uint AskCount { get; init; }
}

internal abstract class DbnMarketByPriceRecord : DbnRecord
{
	public long Price { get; init; }
	public uint Size { get; init; }
	public byte Action { get; init; }
	public byte Side { get; init; }
	public byte Flags { get; init; }
	public byte Depth { get; init; }
	public ulong ReceiveTimestamp { get; init; }
	public int InTimestampDelta { get; init; }
	public uint Sequence { get; init; }
}

internal sealed class DbnTradeRecord : DbnMarketByPriceRecord
{
}

internal sealed class DbnMbp1Record : DbnMarketByPriceRecord
{
	public DbnBidAskPair Level { get; init; }
}

internal sealed class DbnMbp10Record : DbnMarketByPriceRecord
{
	public DbnBidAskPair[] Levels { get; init; }
}

internal sealed class DbnMboRecord : DbnRecord
{
	public ulong OrderId { get; init; }
	public long Price { get; init; }
	public uint Size { get; init; }
	public byte Flags { get; init; }
	public byte ChannelId { get; init; }
	public byte Action { get; init; }
	public byte Side { get; init; }
	public ulong ReceiveTimestamp { get; init; }
	public int InTimestampDelta { get; init; }
	public uint Sequence { get; init; }
}

internal sealed class DbnOhlcvRecord : DbnRecord
{
	public long Open { get; init; }
	public long High { get; init; }
	public long Low { get; init; }
	public long Close { get; init; }
	public ulong Volume { get; init; }
}

internal sealed class DbnStatusRecord : DbnRecord
{
	public ulong ReceiveTimestamp { get; init; }
	public DbnStatusActions Action { get; init; }
	public ushort Reason { get; init; }
	public ushort TradingEvent { get; init; }
	public byte IsTrading { get; init; }
	public byte IsQuoting { get; init; }
	public byte IsShortSaleRestricted { get; init; }
}

internal sealed class DbnStatisticsRecord : DbnRecord
{
	public ulong ReceiveTimestamp { get; init; }
	public ulong ReferenceTimestamp { get; init; }
	public long Price { get; init; }
	public long Quantity { get; init; }
	public uint Sequence { get; init; }
	public int InTimestampDelta { get; init; }
	public DbnStatisticTypes StatisticType { get; init; }
	public ushort ChannelId { get; init; }
	public byte UpdateAction { get; init; }
	public byte Flags { get; init; }
}

internal sealed class DbnImbalanceRecord : DbnRecord
{
	public ulong ReceiveTimestamp { get; init; }
	public long ReferencePrice { get; init; }
	public ulong AuctionTimestamp { get; init; }
	public long IndicativeMatchPrice { get; init; }
	public uint PairedQuantity { get; init; }
	public uint TotalImbalanceQuantity { get; init; }
	public byte Side { get; init; }
}

internal sealed class DbnInstrumentDefinitionRecord : DbnRecord
{
	public ulong ReceiveTimestamp { get; init; }
	public long MinimumPriceIncrement { get; init; }
	public long DisplayFactor { get; init; }
	public ulong Expiration { get; init; }
	public ulong Activation { get; init; }
	public long HighLimitPrice { get; init; }
	public long LowLimitPrice { get; init; }
	public long UnitOfMeasureQuantity { get; init; }
	public long StrikePrice { get; init; }
	public ulong RawInstrumentId { get; init; }
	public uint UnderlyingId { get; init; }
	public int MinimumLotSize { get; init; }
	public int MinimumRoundLotSize { get; init; }
	public int ContractMultiplier { get; init; }
	public int OriginalContractSize { get; init; }
	public ushort MaturityYear { get; init; }
	public string Currency { get; init; }
	public string SettlementCurrency { get; init; }
	public string SecuritySubtype { get; init; }
	public string RawSymbol { get; init; }
	public string Group { get; init; }
	public string Exchange { get; init; }
	public string Asset { get; init; }
	public string Cfi { get; init; }
	public string SecurityType { get; init; }
	public string UnitOfMeasure { get; init; }
	public string Underlying { get; init; }
	public string StrikePriceCurrency { get; init; }
	public byte InstrumentClass { get; init; }
	public byte SecurityUpdateAction { get; init; }
	public byte MaturityMonth { get; init; }
	public byte MaturityDay { get; init; }
	public byte MaturityWeek { get; init; }
}

internal sealed class DbnSymbolMappingRecord : DbnRecord
{
	public byte InputSymbology { get; init; }
	public string InputSymbol { get; init; }
	public byte OutputSymbology { get; init; }
	public string OutputSymbol { get; init; }
	public ulong StartTimestamp { get; init; }
	public ulong EndTimestamp { get; init; }
}

internal sealed class DbnSystemRecord : DbnRecord
{
	public string Message { get; init; }
	public DbnSystemCodes Code { get; init; }
}

internal sealed class DbnErrorRecord : DbnRecord
{
	public string Message { get; init; }
	public byte Code { get; init; }
	public bool IsLast { get; init; }
}

internal sealed class DbnMappingInterval
{
	public uint StartDate { get; init; }
	public uint EndDate { get; init; }
	public string Symbol { get; init; }
}

internal sealed class DbnMetadataMapping
{
	public string RawSymbol { get; init; }
	public DbnMappingInterval[] Intervals { get; init; }
}

internal sealed class DbnMetadata
{
	public byte Version { get; init; }
	public string Dataset { get; init; }
	public DbnSchemas? Schema { get; init; }
	public ulong Start { get; init; }
	public ulong? End { get; init; }
	public ulong? Limit { get; init; }
	public byte? InputSymbology { get; init; }
	public byte OutputSymbology { get; init; }
	public bool HasOutputTimestamp { get; init; }
	public int SymbolLength { get; init; }
	public string[] Symbols { get; init; }
	public string[] PartialSymbols { get; init; }
	public string[] NotFoundSymbols { get; init; }
	public DbnMetadataMapping[] Mappings { get; init; }
}
