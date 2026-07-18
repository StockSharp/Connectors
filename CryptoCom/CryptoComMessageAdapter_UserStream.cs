namespace StockSharp.CryptoCom;

public partial class CryptoComMessageAdapter
{
	private async ValueTask OnUserOrderAsync(CryptoComWsEnvelope<CryptoComOrder> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var order in envelope.Result?.Data ?? [])
		{
			var transactionId = ParseTransactionId(order.ClientOrderId);
			await SendOrderAsync(order, transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
				cancellationToken);
		}
	}

	private async ValueTask OnUserTradeAsync(CryptoComWsEnvelope<CryptoComUserTrade> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var trade in envelope.Result?.Data ?? [])
		{
			var transactionId = ParseTransactionId(trade.ClientOrderId);
			await SendUserTradeAsync(trade,
				transactionId != 0 ? transactionId : _orderStatusSubscriptionId, cancellationToken);
		}
	}

	private async ValueTask OnBalanceAsync(CryptoComWsEnvelope<CryptoComBalance> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var balance in envelope.Result?.Data ?? [])
			await SendBalanceAsync(balance, _portfolioSubscriptionId, cancellationToken);
	}

	private async ValueTask OnPositionAsync(CryptoComWsEnvelope<CryptoComPosition> envelope,
		CancellationToken cancellationToken)
	{
		foreach (var position in envelope.Result?.Data ?? [])
			await SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);
	}
}
