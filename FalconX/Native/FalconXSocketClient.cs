namespace StockSharp.FalconX.Native;

abstract class FalconXSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly FalconXAuthenticator _authenticator;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = { new StringEnumConverter() },
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authentication;
	private bool _isAuthenticated;
	private bool _isRestore;
	private bool _isDisposed;

	protected FalconXSocketClient(string endpoint,
		FalconXAuthenticator authenticator, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = NormalizeEndpoint(endpoint);
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	protected string Path => _endpoint.PathAndQuery;

	protected bool IsConnected => _client?.IsConnected == true;

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _connectSync.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
			Task authentication;
			using (_sync.EnterScope())
				authentication = _authentication?.Task;
			if (authentication is null)
				throw new InvalidOperationException(
					"FalconX WebSocket did not start authentication.");
			await authentication.WaitAsync(TimeSpan.FromSeconds(30),
				cancellationToken);
		}
		finally
		{
			_connectSync.Release();
		}
	}

	public virtual async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		TaskCompletionSource<bool> authentication;
		using (_sync.EnterScope())
		{
			authentication = _authentication;
			_authentication = null;
			_isAuthenticated = false;
		}
		authentication?.TrySetCanceled(cancellationToken);
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	protected async ValueTask SendAsync<TRequest>(TRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"FalconX WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	protected TResponse Deserialize<TResponse>(string payload)
		=> JsonConvert.DeserializeObject<TResponse>(payload, _settings) ??
			throw new InvalidDataException(
				"FalconX WebSocket returned an empty JSON message.");

	protected virtual ValueTask OnAuthenticatedAsync(bool isRestore,
		CancellationToken cancellationToken)
		=> default;

	protected abstract ValueTask OnMessageAsync(string payload,
		FalconXSocketHeader header, CancellationToken cancellationToken);

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint.AbsoluteUri,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Connected or ConnectionStates.Restored)
		{
			TaskCompletionSource<bool> authentication;
			using (_sync.EnterScope())
			{
				_isAuthenticated = false;
				_isRestore = state == ConnectionStates.Restored;
				_authentication = authentication = new(
					TaskCreationOptions.RunContinuationsAsynchronously);
			}
			try
			{
				var requestId = Guid.NewGuid().ToString("D");
				await SendAsync(_authenticator.CreateSocketRequest(Path, requestId),
					cancellationToken);
			}
			catch (Exception error)
			{
				authentication.TrySetException(error);
				await RaiseErrorAsync(error, cancellationToken);
			}
			return;
		}
		if (state == ConnectionStates.Disconnected)
		{
			using (_sync.EnterScope())
				_isAuthenticated = false;
			return;
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<FalconXSocketHeader>(payload);
			if (header.Event == FalconXSocketEvents.AuthenticationResponse)
			{
				await ProcessAuthenticationAsync(payload, cancellationToken);
				return;
			}
			using (_sync.EnterScope())
				if (!_isAuthenticated)
					throw new InvalidOperationException(
						"FalconX WebSocket sent data before authentication.");
			await OnMessageAsync(payload, header, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessAuthenticationAsync(string payload,
		CancellationToken cancellationToken)
	{
		var response =
			Deserialize<FalconXSocketResponse<FalconXSocketAuthenticationBody>>(
				payload);
		TaskCompletionSource<bool> authentication;
		bool isRestore;
		using (_sync.EnterScope())
		{
			authentication = _authentication;
			isRestore = _isRestore;
		}
		if (response.Status != FalconXSocketStatuses.Success)
		{
			var error = new InvalidOperationException(
				"FalconX WebSocket authentication failed: " +
				(response.Error.GetMessage() ?? response.Body?.Message ??
					"unknown error"));
			authentication?.TrySetException(error);
			await RaiseErrorAsync(error, cancellationToken);
			return;
		}
		try
		{
			using (_sync.EnterScope())
				_isAuthenticated = true;
			await OnAuthenticatedAsync(isRestore, cancellationToken);
			authentication?.TrySetResult(true);
			if (isRestore && StateChanged is { } handler)
				await handler(ConnectionStates.Restored, cancellationToken);
		}
		catch (Exception error)
		{
			authentication?.TrySetException(error);
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static Uri NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) ||
			address.Scheme != "wss" ||
			address.AbsolutePath.IsEmpty() || address.AbsolutePath == "/")
			throw new ArgumentException(
				"FalconX WebSocket endpoint must use WSS and include a path.",
				nameof(endpoint));
		return address;
	}

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
