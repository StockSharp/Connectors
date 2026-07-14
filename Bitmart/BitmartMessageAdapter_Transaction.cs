namespace StockSharp.Bitmart;

public partial class BitmartMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.RegisterOrderAsync(regMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.CancelOrderAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.CancelOrderGroupAsync(cancelMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.PortfolioLookupAsync(lookupMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.OrderStatusAsync(statusMsg, cancellationToken);
}