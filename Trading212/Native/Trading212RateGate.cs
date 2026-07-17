namespace StockSharp.Trading212.Native;

sealed class Trading212RateGate
{
	private readonly TimeSpan _interval;
	private readonly Lock _sync = new();
	private DateTime _nextRequest;

	public Trading212RateGate(TimeSpan interval)
	{
		_interval = interval;
	}

	public Task Wait(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DateTime scheduled;
		using (_sync.EnterScope())
		{
			var now = DateTime.UtcNow;
			scheduled = _nextRequest > now ? _nextRequest : now;
			_nextRequest = scheduled + _interval;
		}
		var delay = scheduled - DateTime.UtcNow;
		return delay > TimeSpan.Zero
			? Task.Delay(delay, cancellationToken)
			: Task.CompletedTask;
	}
}
