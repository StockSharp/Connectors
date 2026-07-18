namespace StockSharp.Toobit;

public partial class ToobitMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
		=> GetAdapter(regMsg.SecurityId).RegisterOrderAsync(regMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> GetAdapter(replaceMsg.SecurityId).ReplaceOrderAsync(replaceMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
		=> GetAdapter(cancelMsg.SecurityId).CancelOrderAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (cancelMsg.SecurityId != default)
			return GetAdapter(cancelMsg.SecurityId).CancelOrderGroupAsync(cancelMsg, cancellationToken);

		return _adapters.CachedValues.Select(a => a.CancelOrderGroupAsync(cancelMsg, cancellationToken)).WhenAll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		await _adapters.CachedValues.Select(a => a.PortfolioLookupAsync(lookupMsg, cancellationToken)).WhenAll();
		if (lookupMsg.IsSubscribe)
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		await _adapters.CachedValues.Select(a => a.OrderStatusAsync(statusMsg, cancellationToken)).WhenAll();
		if (statusMsg.IsSubscribe)
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}
}
