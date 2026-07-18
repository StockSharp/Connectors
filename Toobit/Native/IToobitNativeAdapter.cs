namespace StockSharp.Toobit.Native;

interface IToobitNativeAdapter : IDisposable
{
	string BoardCode { get; }
	SecurityTypes SecurityType { get; }

	event Func<Message, CancellationToken, ValueTask> NewOutMessage;

	ValueTask ConnectAsync(CancellationToken cancellationToken);
	void Disconnect();
	ValueTask ResetAsync(CancellationToken cancellationToken);
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
