namespace StockSharp.Shioaji.Native;

sealed class ShioajiSseClient : BaseLogReceiver
{
	private readonly ShioajiRestClient _rest;
	private readonly int _reconnectAttempts;
	private CancellationTokenSource _source;
	private Task _runTask;
	private TaskCompletionSource<bool> _initialConnection;

	public ShioajiSseClient(ShioajiRestClient rest, int reconnectAttempts)
	{
		_rest = rest ?? throw new ArgumentNullException(nameof(rest));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Shioaji) + "_SSE";

	public event Func<CancellationToken, ValueTask> BeforeConnect;
	public event Func<string, string, CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	protected override void DisposeManaged()
	{
		_source?.Cancel();
		_source?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Start(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("The Shioaji SSE stream is already running.");

		_source = new CancellationTokenSource();
		_initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_runTask = Run(_source.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async ValueTask Stop(CancellationToken cancellationToken)
	{
		if (_runTask == null)
			return;

		_source.Cancel();
		try
		{
			await _runTask.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (_source.IsCancellationRequested)
		{
		}
		finally
		{
			_runTask = null;
			_source.Dispose();
			_source = null;
			_initialConnection = null;
		}
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				if (BeforeConnect is { } beforeConnect)
					await beforeConnect(cancellationToken);

				using var response = await _rest.OpenStream(cancellationToken);
				_initialConnection?.TrySetResult(true);
				attempt = 0;
				await ReadEvents(response, cancellationToken);
				throw new IOException("The Shioaji SSE stream was closed by the server.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				attempt++;
				this.AddWarningLog("Shioaji SSE connection failed ({0}/{1}): {2}",
					attempt, _reconnectAttempts + 1, ex.Message);
				if (attempt > _reconnectAttempts)
				{
					_initialConnection?.TrySetException(ex);
					if (Error is { } errorHandler)
						await errorHandler(ex, cancellationToken);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1))), cancellationToken);
			}
		}
	}

	private async Task ReadEvents(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8);
		var eventName = string.Empty;
		var data = new StringBuilder();

		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line == null)
				break;

			if (line.Length == 0)
			{
				if (data.Length > 0 && EventReceived is { } handler)
					await handler(eventName, data.ToString(), cancellationToken);
				eventName = string.Empty;
				data.Clear();
				continue;
			}

			if (line[0] == ':')
				continue;
			if (line.StartsWith("event:", StringComparison.Ordinal))
			{
				eventName = line[6..].TrimStart();
				continue;
			}
			if (line.StartsWith("data:", StringComparison.Ordinal))
			{
				if (data.Length > 0)
					data.AppendLine();
				data.Append(line[5..].TrimStart());
			}
		}

		if (data.Length > 0 && EventReceived is { } finalHandler)
			await finalHandler(eventName, data.ToString(), cancellationToken);
	}
}
