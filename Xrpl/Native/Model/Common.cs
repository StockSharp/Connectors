namespace StockSharp.Xrpl.Native.Model;

sealed class XrplAsset
{
	public string CurrencyCode { get; init; }
	public string Issuer { get; init; }
	public string Symbol { get; init; }
	public bool IsXrp { get; init; }

	public string Key => IsXrp
		? "XRP"
		: CurrencyCode + ":" + Issuer;

	public string BookChangeId => IsXrp
		? "XRP_drops"
		: Issuer + "/" + CurrencyCode;
}

sealed class XrplMarket
{
	public string SecurityCode { get; set; }
	public XrplAsset Base { get; init; }
	public XrplAsset Quote { get; init; }
	public string DomainId { get; init; }
}

sealed class XrplBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public string OfferId { get; init; }
}

sealed class XrplBook
{
	public XrplBookLevel[] Bids { get; init; }
	public XrplBookLevel[] Asks { get; init; }
	public uint LedgerIndex { get; init; }
	public DateTime Time { get; init; }
}

sealed class XrplMarketBar
{
	public string Id { get; init; }
	public uint LedgerIndex { get; init; }
	public DateTime Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
}

sealed class XrplCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int LedgerCount { get; init; }
}

sealed class XrplLedgerPoint
{
	public uint Index { get; init; }
	public DateTime Time { get; init; }
}

sealed class XrplAccountState
{
	public string Account { get; init; }
	public decimal XrpBalance { get; init; }
	public uint Sequence { get; init; }
	public uint LedgerIndex { get; init; }
}

sealed class XrplBalance
{
	public XrplAsset Asset { get; init; }
	public decimal Current { get; init; }
}

sealed class XrplAccountOffer
{
	public uint Sequence { get; init; }
	public XrplMarket Market { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal Balance { get; init; }
	public DateTime? Expiration { get; init; }
}

sealed class XrplSubmitResult
{
	public string Hash { get; init; }
	public string EngineResult { get; init; }
	public string Message { get; init; }
	public uint? LedgerIndex { get; init; }
}

sealed class XrplTransactionStatus
{
	public string Hash { get; init; }
	public bool Validated { get; init; }
	public string Result { get; init; }
	public DateTime? Time { get; init; }
	public uint? LedgerIndex { get; init; }
	public uint? Sequence { get; init; }
	public JObject Transaction { get; init; }
	public JObject Metadata { get; init; }
}

sealed class XrplSignedTransaction
{
	public string Blob { get; init; }
	public string Hash { get; init; }
	public uint Sequence { get; init; }
}
