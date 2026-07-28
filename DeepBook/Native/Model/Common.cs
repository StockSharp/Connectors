namespace StockSharp.DeepBook.Native.Model;

sealed class DeepBookToken
{
	public string CoinType { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class DeepBookSharedObject
{
	public string ObjectId { get; init; }
	public ulong InitialVersion { get; init; }
	public bool IsMutable { get; init; }
}

sealed class DeepBookMarket
{
	public string PoolId { get; init; }
	public ulong PoolInitialVersion { get; set; }
	public string PoolName { get; init; }
	public DeepBookToken BaseToken { get; init; }
	public DeepBookToken QuoteToken { get; init; }
	public string SecurityCode { get; set; }
	public decimal MinSize { get; init; }
	public decimal LotSize { get; init; }
	public decimal TickSize { get; init; }
}

sealed class DeepBookBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class DeepBookOrderBook
{
	public DateTime Time { get; init; }
	public DeepBookBookLevel[] Bids { get; init; }
	public DeepBookBookLevel[] Asks { get; init; }
}

sealed class DeepBookTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal BaseVolume { get; init; }
	public decimal QuoteVolume { get; init; }
	public Sides Side { get; init; }
}

sealed class DeepBookCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
}

sealed class DeepBookQuote
{
	public Sides Side { get; init; }
	public ulong InputAmount { get; init; }
	public ulong OutputAmount { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class DeepBookPreparedTransaction
{
	public Bcs Transaction { get; init; }
	public GasCostSummary GasUsed { get; init; }
}

sealed class DeepBookTransactionReceipt
{
	public string TransactionDigest { get; init; }
	public bool IsSuccessful { get; init; }
	public string Error { get; init; }
	public DateTime Time { get; init; }
	public ulong? Checkpoint { get; init; }
	public GasCostSummary GasUsed { get; init; }
}

sealed class DeepBookApiException : InvalidOperationException
{
	public DeepBookApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
