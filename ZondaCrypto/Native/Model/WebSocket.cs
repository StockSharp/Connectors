namespace StockSharp.ZondaCrypto.Native.Model;

sealed class ZondaCryptoWsMessage
{
	public string Action { get; init; }

	public string Topic { get; init; }

	public string Module { get; init; }

	public string Path { get; init; }

	public DateTime Time { get; init; }

	public long Sequence { get; init; }

	public ZondaCryptoTicker Ticker { get; init; }

	public ZondaCryptoBookChange[] BookChanges { get; init; } = [];

	public ZondaCryptoTrade[] Trades { get; init; } = [];

	public ZondaCryptoWallet Wallet { get; init; }

	public ZondaCryptoOffer Offer { get; init; }
}
