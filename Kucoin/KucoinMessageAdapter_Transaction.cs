namespace StockSharp.Kucoin;

public partial class KucoinMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
		=> GetAdapter(regMsg).RegisterOrderAsync(regMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> GetAdapter(cancelMsg).CancelOrderAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (_spotAdapter is INativeAdapter spot)
			await spot.CancelOrderGroupAsync(cancelMsg, cancellationToken);

		if (_futuresAdapter is INativeAdapter futures)
			await futures.CancelOrderGroupAsync(cancelMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (_spotAdapter is INativeAdapter spot)
			await spot.PortfolioLookupAsync(lookupMsg, cancellationToken);

		if (_futuresAdapter is INativeAdapter futures)
			await futures.PortfolioLookupAsync(lookupMsg, cancellationToken);

		if (lookupMsg.IsSubscribe)
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (_spotAdapter is INativeAdapter spot)
			await spot.OrderStatusAsync(statusMsg, cancellationToken);

		if (_futuresAdapter is INativeAdapter futures)
			await futures.OrderStatusAsync(statusMsg, cancellationToken);

		if (statusMsg.IsSubscribe)
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}
}