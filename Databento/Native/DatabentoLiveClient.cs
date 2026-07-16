namespace StockSharp.Databento.Native;

internal sealed class DatabentoLiveClient : BaseLogReceiver
{
	private const int _defaultPort = 13000;
	private const int _maximumControlLineLength = 64 * 1024;

	private readonly string _dataset;
	private readonly string _apiKey;
	private readonly string _address;
	private readonly TimeSpan _heartbeatInterval;
	private readonly int _reconnectAttempts;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly object _subscriptionsSync = new();
	private readonly Dictionary<string, DatabentoLiveSubscription> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _sentSubscriptions = new(StringComparer.Ordinal);

	private TcpClient _tcpClient;
	private NetworkStream _stream;
	private DbnDecoder _decoder;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private bool _isStarted;
	private bool _isStopping;
	private uint _nextSubscriptionId;

	public DatabentoLiveClient(string dataset, string apiKey, string address,
		TimeSpan heartbeatInterval, int reconnectAttempts)
	{
		_dataset = dataset.ThrowIfEmpty(nameof(dataset));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_address = address;
		_heartbeatInterval = heartbeatInterval;
		_reconnectAttempts = Math.Max(0, reconnectAttempts);
	}

	public override string Name => nameof(DatabentoLiveClient);

	public event Func<DbnRecord, CancellationToken, ValueTask> RecordReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<CancellationToken, ValueTask> Reconnected;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_tcpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (_apiKey.Length != 32)
			throw new ArgumentException("A Databento API key must contain 32 characters.", nameof(_apiKey));
		ValidateControlValue(_apiKey, nameof(_apiKey));
		ValidateControlValue(_dataset, nameof(_dataset));
		if (_heartbeatInterval < TimeSpan.FromSeconds(5))
			throw new ArgumentOutOfRangeException(nameof(_heartbeatInterval), _heartbeatInterval,
				"The Databento heartbeat interval must be at least five seconds.");

		_isStopping = false;
		_lifetime = new();
		await OpenAndAuthenticate(cancellationToken);
	}

	public async Task Subscribe(DatabentoLiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		lock (_subscriptionsSync)
			_subscriptions[subscription.Key] = subscription;

		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			if (_stream == null)
				throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
			if (_sentSubscriptions.Add(subscription.Key))
				await SendControl(subscription.ToRequest(NextSubscriptionId()), cancellationToken);

			if (!_isStarted)
			{
				await StartSession(cancellationToken);
				_receiveTask = ReceiveLoop(_lifetime.Token);
			}
		}
		catch
		{
			lock (_subscriptionsSync)
				_subscriptions.Remove(subscription.Key);
			throw;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public void Unsubscribe(string key)
	{
		if (key.IsEmpty())
			return;
		lock (_subscriptionsSync)
			_subscriptions.Remove(key);
	}

	public async Task Disconnect(CancellationToken cancellationToken)
	{
		_isStopping = true;
		_lifetime?.Cancel();
		CloseSocket();

		var receiveTask = _receiveTask;
		if (receiveTask != null)
		{
			try
			{
				await receiveTask.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested ||
				_lifetime?.IsCancellationRequested == true)
			{
			}
			catch (Exception) when (_isStopping)
			{
			}
		}

		_receiveTask = null;
		_lifetime?.Dispose();
		_lifetime = null;
		lock (_subscriptionsSync)
		{
			_subscriptions.Clear();
			_sentSubscriptions.Clear();
		}
	}

	private async Task OpenAndAuthenticate(CancellationToken cancellationToken)
	{
		CloseSocket();
		var (host, port) = ParseAddress(_address, _dataset);
		_tcpClient = new() { NoDelay = true };
		await _tcpClient.ConnectAsync(host, port, cancellationToken);
		_stream = _tcpClient.GetStream();

		var greeting = await ReadControlLine(_stream, cancellationToken);
		if (greeting.IsEmpty())
			throw new InvalidDataException("The Databento live gateway returned an empty greeting.");
		var challengeLine = await ReadControlLine(_stream, cancellationToken);
		if (!challengeLine.StartsWith("cram=", StringComparison.Ordinal))
			throw new InvalidDataException("The Databento live gateway did not return a CRAM challenge.");

		var challenge = challengeLine[5..];
		var challengeKey = Encoding.ASCII.GetBytes($"{challenge}|{_apiKey}");
		var hash = Convert.ToHexString(SHA256.HashData(challengeKey)).ToLowerInvariant();
		var bucketId = _apiKey[^5..];
		var heartbeatSeconds = checked((long)Math.Ceiling(_heartbeatInterval.TotalSeconds));
		await SendControl(
			$"auth={hash}-{bucketId}|dataset={_dataset}|encoding=dbn|compression=none|ts_out=0|client=stocksharp/1.0|heartbeat_interval_s={heartbeatSeconds}|slow_reader_behavior=disconnect\n",
			cancellationToken);

		var response = DatabentoAuthenticationResponse.Parse(
			await ReadControlLine(_stream, cancellationToken));
		if (!response.IsSuccess)
			throw new UnauthorizedAccessException(response.Error.IsEmpty("Databento authentication failed."));

		this.AddInfoLog("Databento live session {0} authenticated for {1}.",
			response.SessionId.IsEmpty("unknown"), _dataset);
		_isStarted = false;
		_decoder = null;
		_sentSubscriptions.Clear();
	}

	private async Task StartSession(CancellationToken cancellationToken)
	{
		await SendControl("start_session\n", cancellationToken);
		_decoder = new();
		await _decoder.ReadMetadata(_stream, cancellationToken);
		_isStarted = true;
	}

	private async Task ReceiveLoop(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				var record = await ReadRecordWithTimeout(cancellationToken);
				if (record == null)
					throw new EndOfStreamException("The Databento live gateway closed the stream.");
				if (RecordReceived != null)
					await RecordReceived(record, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex) when (!_isStopping)
			{
				this.AddWarningLog("Databento live stream interrupted: {0}", ex.Message);
				if (!await TryReconnect(cancellationToken))
				{
					if (Error != null)
						await Error(ex, CancellationToken.None);
					return;
				}
			}
		}
	}

	private async Task<DbnRecord> ReadRecordWithTimeout(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_heartbeatInterval + TimeSpan.FromSeconds(5));
		try
		{
			return await _decoder.ReadRecord(_stream, timeout.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException("The Databento live gateway did not send data or a heartbeat in time.");
		}
	}

	private async Task<bool> TryReconnect(CancellationToken cancellationToken)
	{
		for (var attempt = 1; attempt <= _reconnectAttempts && !cancellationToken.IsCancellationRequested; attempt++)
		{
			try
			{
				var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt - 1, 5)));
				await Task.Delay(delay, cancellationToken);
				await _writeLock.WaitAsync(cancellationToken);
				try
				{
					await OpenAndAuthenticate(cancellationToken);
					DatabentoLiveSubscription[] subscriptions;
					lock (_subscriptionsSync)
						subscriptions = _subscriptions.Values.ToArray();
					foreach (var subscription in subscriptions)
					{
						await SendControl(subscription.ToRequest(NextSubscriptionId()), cancellationToken);
						_sentSubscriptions.Add(subscription.Key);
					}
					await StartSession(cancellationToken);
				}
				finally
				{
					_writeLock.Release();
				}

				this.AddInfoLog("Databento live session restored on attempt {0}.", attempt);
				if (Reconnected != null)
					await Reconnected(cancellationToken);
				return true;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return false;
			}
			catch (Exception ex)
			{
				this.AddWarningLog("Databento reconnect attempt {0} failed: {1}", attempt, ex.Message);
			}
		}
		return false;
	}

	private async Task SendControl(string value, CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var bytes = Encoding.ASCII.GetBytes(value);
		await stream.WriteAsync(bytes, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	private uint NextSubscriptionId()
	{
		var id = unchecked(++_nextSubscriptionId);
		if (id == 0)
			id = unchecked(++_nextSubscriptionId);
		return id;
	}

	private static async Task<string> ReadControlLine(Stream stream,
		CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		var one = new byte[1];
		while (buffer.Length < _maximumControlLineLength)
		{
			var read = await stream.ReadAsync(one, cancellationToken);
			if (read == 0)
				throw new EndOfStreamException("The Databento gateway closed the control stream.");
			if (one[0] == (byte)'\n')
				return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)).TrimEnd('\r');
			buffer.WriteByte(one[0]);
		}
		throw new InvalidDataException("The Databento gateway returned an oversized control line.");
	}

	private static (string host, int port) ParseAddress(string address, string dataset)
	{
		if (address.IsEmpty())
			return ($"{dataset.Replace('.', '-').ToLowerInvariant()}.lsg.databento.com", _defaultPort);

		var normalized = address.Contains("://", StringComparison.Ordinal)
			? address
			: $"tcp://{address}";
		if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Host.IsEmpty())
			throw new ArgumentException("The Databento live address must contain a host and port.", nameof(address));
		return (uri.Host, uri.IsDefaultPort ? _defaultPort : uri.Port);
	}

	private static void ValidateControlValue(string value, string name)
	{
		if (value.IndexOfAny(['|', '\r', '\n']) >= 0)
			throw new ArgumentException("A Databento control value contains a reserved delimiter.", name);
	}

	private void CloseSocket()
	{
		_isStarted = false;
		_decoder = null;
		_stream?.Dispose();
		_stream = null;
		_tcpClient?.Dispose();
		_tcpClient = null;
	}

	protected override void DisposeManaged()
	{
		_isStopping = true;
		_lifetime?.Cancel();
		CloseSocket();
		_lifetime?.Dispose();
		_writeLock.Dispose();
		base.DisposeManaged();
	}
}
