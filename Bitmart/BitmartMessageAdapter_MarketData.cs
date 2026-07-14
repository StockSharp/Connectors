namespace StockSharp.Bitmart;

public partial class BitmartMessageAdapter
{
	/// <inheritdoc/>
	protected override ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.SecurityLookupAsync(lookupMsg, cancellationToken);

	/// <inheritdoc/>
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.Level1SubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc/>
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.MarketDepthSubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc/>
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.TicksSubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc/>
	protected override ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> _nativeAdapter.TFCandlesSubscriptionAsync(mdMsg, cancellationToken);
}