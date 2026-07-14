namespace StockSharp.CoinEx;

public partial class CoinExMessageAdapter
{
	private readonly SynchronizedDictionary<long, NativeAdapter> _adaptersByTransId = [];

	private NativeAdapter GetAdapter(long transId)
		=> _adaptersByTransId[transId];

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var adapter = GetAdapter(regMsg.SecurityId);
		_adaptersByTransId.Add(regMsg.TransactionId, adapter);
		return adapter.RegisterOrder(regMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var adapter = GetAdapter(replaceMsg.OriginalTransactionId);
		return adapter.ReplaceOrder(replaceMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var adapter = GetAdapter(cancelMsg.OriginalTransactionId);
		return adapter.CancelOrder(cancelMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (_spot is NativeAdapter spot)
			await spot.PortfolioLookup(lookupMsg, cancellationToken);

		if (_futures is NativeAdapter fut)
			await fut.PortfolioLookup(lookupMsg, cancellationToken);

		if (lookupMsg.IsSubscribe)
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (_spot is NativeAdapter spot)
			await spot.OrderStatus(statusMsg, cancellationToken);

		if (_futures is NativeAdapter fut)
			await fut.OrderStatus(statusMsg, cancellationToken);

		if (statusMsg.IsSubscribe)
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}
}