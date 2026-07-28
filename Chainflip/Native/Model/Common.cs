namespace StockSharp.Chainflip.Native.Model;

sealed class ChainflipAsset
{
	public string Chain { get; init; }
	public string Symbol { get; init; }
	public int Decimals { get; init; }
	public string ContractAddress { get; init; }

	public string Key => $"{Chain}:{Symbol}";
	public bool IsEvm => Chain.EqualsIgnoreCase("Ethereum") ||
		Chain.EqualsIgnoreCase("Arbitrum");
	public bool IsNative => IsEvm && ContractAddress.IsEmpty();
}

sealed class ChainflipMarket
{
	public ChainflipAsset BaseAsset { get; init; }
	public ChainflipAsset QuoteAsset { get; init; }
	public string SecurityCode { get; init; }

	public string Key => $"{BaseAsset.Key}/{QuoteAsset.Key}";
}

sealed class ChainflipLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class ChainflipOrderBook
{
	public DateTime Time { get; init; }
	public ChainflipLevel[] Bids { get; init; }
	public ChainflipLevel[] Asks { get; init; }
}

sealed class ChainflipTrade
{
	public string Id { get; init; }
	public ChainflipMarket Market { get; init; }
	public DateTime Time { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class ChainflipBlockTrades
{
	public long BlockNumber { get; init; }
	public string BlockHash { get; init; }
	public DateTime Time { get; init; }
	public ChainflipTrade[] Trades { get; init; }
}

sealed class ChainflipTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}
