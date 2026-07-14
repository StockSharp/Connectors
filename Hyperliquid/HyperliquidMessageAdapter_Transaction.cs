namespace StockSharp.Hyperliquid;

public partial class HyperliquidMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
		=> EnsureGetAdapter(regMsg.SecurityId).RegisterOrderAsync(regMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
		=> EnsureGetAdapter(replaceMsg.SecurityId).ReplaceOrderAsync(replaceMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> EnsureGetAdapter(cancelMsg.SecurityId).CancelOrderAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.SecurityId != default)
			return EnsureGetAdapter(cancelMsg.SecurityId).CancelOrderGroupAsync(cancelMsg, cancellationToken);

		return NativeAdapters.Values.Select(a => a.CancelOrderGroupAsync(cancelMsg, cancellationToken)).WhenAll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		await NativeAdapters.Values.Select(a => a.PortfolioLookupAsync(lookupMsg, cancellationToken)).WhenAll();

		if (!lookupMsg.IsSubscribe)
			return;

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		await NativeAdapters.Values.Select(a => a.OrderStatusAsync(statusMsg, cancellationToken)).WhenAll();

		if (!statusMsg.IsSubscribe)
			return;

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}
}
