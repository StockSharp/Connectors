namespace StockSharp.Ligther.Native;

interface INativeAdapter
{
	string BoardCode { get; }

	event Func<Message, CancellationToken, ValueTask> NewOutMessage;

	ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken);
	ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken);
	ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken);
	ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken);

	ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);

	ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken);
	ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);

	ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
}

