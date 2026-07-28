namespace StockSharp.Coinmetro.Native.Model;

sealed class CoinmetroWsMessage
{
	public CoinmetroTicker Tick { get; init; }

	public CoinmetroBookUpdate BookUpdate { get; init; }

	public JObject OrderStatus { get; init; }

	public CoinmetroWallet WalletUpdate { get; init; }
}
