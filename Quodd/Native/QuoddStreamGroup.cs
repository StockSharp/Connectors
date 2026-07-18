namespace StockSharp.Quodd.Native;

sealed class QuoddStreamGroup : IAsyncDisposable
{
	private readonly QuoddClient _client;
	private readonly QuoddAssetTypes _assetType;
	private readonly int _maxAttempts;
	private readonly HashSet<string> _tickers = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _changed = new(0, 1);
	private readonly CancellationTokenSource _stop = new();
	private CancellationTokenSource _activeCall;
	private Task _runner;

	public QuoddStreamGroup(QuoddClient client, QuoddAssetTypes assetType, int maxAttempts)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_assetType = assetType;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public event Func<SnapMessage, QuoddAssetTypes, CancellationToken, ValueTask> SnapReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public void Start()
	{
		if (_runner != null)
			throw new InvalidOperationException($"QUODD {_assetType} stream is already running.");
		_runner = Run(_stop.Token);
	}

	public void Add(string ticker)
	{
		ticker = ticker.ThrowIfEmpty(nameof(ticker));
		using (var scope = _sync.EnterScope())
		{
			if (!_tickers.Add(ticker))
				return;
		}
		SignalChange();
	}

	public void Remove(string ticker)
	{
		using (var scope = _sync.EnterScope())
		{
			if (!_tickers.Remove(ticker))
				return;
		}
		SignalChange();
	}

	private void SignalChange()
	{
		CancellationTokenSource active;
		using (var scope = _sync.EnterScope())
			active = _activeCall;
		try
		{
			active?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		try
		{
			_changed.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	private string[] Snapshot()
	{
		using var scope = _sync.EnterScope();
		return [.. _tickers.OrderBy(ticker => ticker, StringComparer.OrdinalIgnoreCase)];
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			var tickers = Snapshot();
			if (tickers.Length == 0)
			{
				await _changed.WaitAsync(cancellationToken);
				continue;
			}

			using var callSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			using (var scope = _sync.EnterScope())
				_activeCall = callSource;
			try
			{
				await _client.StreamSnaps(tickers, _assetType, OnSnap, callSource.Token);
				if (!callSource.IsCancellationRequested)
					throw new QuoddApiException($"QUODD {_assetType} stream ended unexpectedly.");
				failures = 0;
			}
			catch (OperationCanceledException) when (callSource.IsCancellationRequested)
			{
				failures = 0;
			}
			catch (Exception ex)
			{
				if (ex is RpcException { StatusCode: StatusCode.Unauthenticated })
					_client.InvalidateToken();
				failures++;
				if (Error != null)
					await Error(ex, cancellationToken);

				var isCoolingOff = failures >= _maxAttempts;
				var delay = isCoolingOff
					? TimeSpan.FromMinutes(1)
					: TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5)));
				try
				{
					await Task.Delay(delay, callSource.Token);
				}
				catch (OperationCanceledException) when (callSource.IsCancellationRequested)
				{
				}
				if (isCoolingOff)
					failures = 0;
			}
			finally
			{
				using var scope = _sync.EnterScope();
				if (ReferenceEquals(_activeCall, callSource))
					_activeCall = null;
			}
		}
	}

	private ValueTask OnSnap(SnapMessage message, CancellationToken cancellationToken)
		=> SnapReceived == null ? default : SnapReceived(message, _assetType, cancellationToken);

	public async ValueTask DisposeAsync()
	{
		_stop.Cancel();
		SignalChange();
		if (_runner != null)
		{
			try
			{
				await _runner;
			}
			catch (OperationCanceledException)
			{
			}
		}
		_stop.Dispose();
		_changed.Dispose();
	}
}
