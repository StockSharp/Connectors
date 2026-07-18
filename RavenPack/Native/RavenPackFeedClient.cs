namespace StockSharp.RavenPack.Native;

sealed class RavenPackFeedClient : BaseLogReceiver
{
	private const int _maxLineLength = 8 * 1024 * 1024;
	private const int _deduplicationWindow = 4096;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
	private readonly CancellationTokenSource _cancellation = new();
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Lock _seenSync = new();
	private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
	private readonly Queue<string> _seenOrder = new();

	private Task _runTask;

	public RavenPackFeedClient(Uri address, string datasetId, string apiKey,
		int maxAttempts)
	{
		if (address == null)
			throw new ArgumentNullException(nameof(address));
		datasetId.ThrowIfEmpty(nameof(datasetId));
		apiKey.ThrowIfEmpty(nameof(apiKey));
		_uri = new(EnsureTrailingSlash(address),
			$"{Uri.EscapeDataString(datasetId)}?keep_alive");
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("API_KEY", apiKey);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-RavenPack/1.0");
	}

	public override string Name => nameof(RavenPack) + "_" + nameof(RavenPackFeedClient);
	public bool IsStopped => _runTask?.IsCompleted == true;

	public event Func<RavenPackAnalyticsRecord, CancellationToken, ValueTask> RecordReceived;
	public event Func<Exception, bool, CancellationToken, ValueTask> Error;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("RavenPack feed is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		if (_runTask == null)
			return;
		try
		{
			await _runTask;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
				using var response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				if (response.StatusCode != HttpStatusCode.OK)
				{
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					throw RavenPackRestClient.CreateError(response.StatusCode, content,
						_uri.AbsolutePath);
				}

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await ReadFeed(response, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new IOException("RavenPack real-time feed closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				var isTerminal = !IsTransient(error) || failures >= _maxAttempts;
				await Invoke(Error, error, isTerminal, CancellationToken.None);
				if (isTerminal)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(
					TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))),
					cancellationToken);
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
	}

	private async Task ReadFeed(HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8, true, 16 * 1024,
			leaveOpen: false);
		while (!cancellationToken.IsCancellationRequested &&
			await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			if (line.IsEmpty())
				continue;
			if (line.Length > _maxLineLength)
				throw new InvalidDataException("RavenPack feed record exceeds 8 MiB.");

			RavenPackAnalyticsRecord record;
			try
			{
				record = JsonConvert.DeserializeObject<RavenPackAnalyticsRecord>(line,
					_jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException("RavenPack feed returned invalid JSON.", error);
			}
			if (record == null || !Remember(record.GetEventKey()))
				continue;
			await Invoke(RecordReceived, record, cancellationToken);
		}
	}

	private bool Remember(string key)
	{
		if (key.IsEmpty())
			return true;
		using var scope = _seenSync.EnterScope();
		if (!_seen.Add(key))
			return false;
		_seenOrder.Enqueue(key);
		while (_seenOrder.Count > _deduplicationWindow)
			_seen.Remove(_seenOrder.Dequeue());
		return true;
	}

	private static bool IsTransient(Exception error)
		=> error is not RavenPackApiException apiError ||
			apiError.StatusCode is HttpStatusCode.RequestTimeout or
				HttpStatusCode.TooManyRequests ||
			(int)apiError.StatusCode is >= 500 and <= 511;

	private static ValueTask Invoke(
		Func<RavenPackAnalyticsRecord, CancellationToken, ValueTask> handler,
		RavenPackAnalyticsRecord record, CancellationToken cancellationToken)
		=> handler == null ? default : handler(record, cancellationToken);

	private static ValueTask Invoke(
		Func<Exception, bool, CancellationToken, ValueTask> handler,
		Exception error, bool isTerminal, CancellationToken cancellationToken)
		=> handler == null ? default : handler(error, isTerminal, cancellationToken);

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
