namespace StockSharp.Cetus.Native;

sealed class CetusCheckpointClient : BaseLogReceiver
{
	private readonly SubscriptionService.SubscriptionServiceClient _client;
	private readonly Func<CetusSwapEvent, ValueTask> _swapHandler;
	private readonly Func<Exception, ValueTask> _errorHandler;
	private readonly Lock _sync = new();
	private AsyncServerStreamingCall<SubscribeCheckpointsResponse> _call;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private TaskCompletionSource<bool> _started;
	private bool _isConnected;
	private bool _isDisposed;

	public CetusCheckpointClient(
		SubscriptionService.SubscriptionServiceClient client,
		Func<CetusSwapEvent, ValueTask> swapHandler,
		Func<Exception, ValueTask> errorHandler)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_swapHandler = swapHandler ?? throw new ArgumentNullException(
			nameof(swapHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(
			nameof(errorHandler));
	}

	public override string Name => "Cetus_Checkpoints";

	public bool IsConnected
	{
		get
		{
			using (_sync.EnterScope())
				return _isConnected;
		}
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		using (_sync.EnterScope())
			if (_call is not null)
				throw new InvalidOperationException(
					"The Cetus checkpoint stream is already initialized.");

		var mask = new FieldMask();
		mask.Paths.AddRange(
		[
			"sequence_number",
			"summary.timestamp",
			"transactions.digest",
			"transactions.timestamp",
			"transactions.events",
		]);
		var lifetime = new CancellationTokenSource();
		var started = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		AsyncServerStreamingCall<SubscribeCheckpointsResponse> call;
		try
		{
			call = _client.SubscribeCheckpoints(new()
			{
				ReadMask = mask,
			}, cancellationToken: lifetime.Token);
		}
		catch
		{
			lifetime.Dispose();
			throw;
		}
		using (_sync.EnterScope())
		{
			_lifetime = lifetime;
			_started = started;
			_call = call;
			_receiveTask = ReceiveLoopAsync(call, lifetime.Token);
		}
		await started.Task.WaitAsync(cancellationToken);
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		CancellationTokenSource lifetime;
		AsyncServerStreamingCall<SubscribeCheckpointsResponse> call;
		TaskCompletionSource<bool> started;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			lifetime = _lifetime;
			call = _call;
			started = _started;
			_lifetime = null;
			_call = null;
			_started = null;
			_receiveTask = null;
		}
		lifetime?.Cancel();
		call?.Dispose();
		lifetime?.Dispose();
		started?.TrySetCanceled();
		base.DisposeManaged();
	}

	private async Task ReceiveLoopAsync(
		AsyncServerStreamingCall<SubscribeCheckpointsResponse> call,
		CancellationToken cancellationToken)
	{
		try
		{
			while (await call.ResponseStream.MoveNext(cancellationToken))
			{
				var response = call.ResponseStream.Current ?? throw new
					InvalidDataException(
						"Sui checkpoint stream returned an empty response.");
				if (response.Checkpoint is null)
					throw new InvalidDataException(
						"Sui checkpoint stream returned no checkpoint.");
				using (_sync.EnterScope())
				{
					_isConnected = true;
					_started?.TrySetResult(true);
				}
				await ProcessCheckpointAsync(response.Checkpoint);
			}
			throw new IOException(
				"Sui checkpoint stream completed unexpectedly.");
		}
		catch (Exception error) when (error is not OperationCanceledException &&
			!cancellationToken.IsCancellationRequested && !_isDisposed)
		{
			var notify = false;
			using (_sync.EnterScope())
			{
				_isConnected = false;
				if (_started is not null && !_started.Task.IsCompleted)
					_started.TrySetException(error);
				else
					notify = true;
			}
			if (notify)
				await _errorHandler(error);
		}
		finally
		{
			using (_sync.EnterScope())
				_isConnected = false;
		}
	}

	private async ValueTask ProcessCheckpointAsync(Checkpoint checkpoint)
	{
		var checkpointTime = checkpoint.Summary?.Timestamp.ToUtc(
			DateTime.UtcNow) ?? DateTime.UtcNow;
		foreach (var transaction in checkpoint.Transactions)
		{
			if (transaction.Digest.IsEmpty() || transaction.Events is null)
				continue;
			var time = transaction.Timestamp.ToUtc(checkpointTime);
			for (var index = 0; index < transaction.Events.Events.Count; index++)
			{
				var item = transaction.Events.Events[index];
				if (!item.EventType.EqualsIgnoreCase(
					CetusExtensions.SwapEventType))
					continue;
				try
				{
					var swap = item.ReadSwapEvent(transaction.Digest, index, time);
					if (swap is not null)
						await _swapHandler(swap);
				}
				catch (Exception error)
				{
					await _errorHandler(error);
				}
			}
		}
	}
}
