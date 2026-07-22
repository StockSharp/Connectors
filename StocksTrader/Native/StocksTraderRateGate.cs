namespace StockSharp.StocksTrader.Native;

sealed class StocksTraderRateGate
{
	private static readonly TimeSpan _minimumInterval = TimeSpan.FromMilliseconds(500);
	private readonly Lock _sync = new();
	private DateTime _nextRequest;

	public Task WaitAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DateTime scheduled;
		using (_sync.EnterScope())
		{
			var now = DateTime.UtcNow;
			scheduled = _nextRequest > now ? _nextRequest : now;
			_nextRequest = scheduled + _minimumInterval;
		}

		var delay = scheduled - DateTime.UtcNow;
		return delay > TimeSpan.Zero
			? Task.Delay(delay, cancellationToken)
			: Task.CompletedTask;
	}
}
