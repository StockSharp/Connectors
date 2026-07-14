namespace StockSharp.BingX;

public partial class BingXMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var adapter = EnsureGetAdapter(regMsg.SecurityId);
		return adapter.RegisterOrder(regMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var adapter = EnsureGetAdapter(replaceMsg.SecurityId);
		return adapter.ReplaceOrder(replaceMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var adapter = EnsureGetAdapter(cancelMsg.SecurityId);
		return adapter.CancelOrder(cancelMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.SecurityId != default)
		{
			var adapter = EnsureGetAdapter(cancelMsg.SecurityId);
			return adapter.CancelGroupOrder(cancelMsg, cancellationToken);
		}
		else
			return _adapters.CachedValues.Select(a => a.CancelGroupOrder(cancelMsg, cancellationToken)).WhenAll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		await _adapters.CachedValues.Select(a => a.PortfolioLookup(lookupMsg, cancellationToken)).WhenAll();

		if (lookupMsg.IsSubscribe)
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		await _adapters.CachedValues.Select(a => a.OrderStatus(statusMsg, cancellationToken)).WhenAll();

		if (statusMsg.IsSubscribe)
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}
}