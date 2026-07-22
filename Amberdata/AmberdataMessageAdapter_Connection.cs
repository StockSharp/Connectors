namespace StockSharp.Amberdata;

public partial class AmberdataMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _socket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		ExchangeFilter = AmberdataExtensions.NormalizeExchange(ExchangeFilter);
		ValidateSocketEndpoint(SocketEndpoint);
		_rest = new(ApiEndpoint, Token, RequestInterval)
		{
			Parent = this,
		};
		try
		{
			await _rest.ValidateAsync(ExchangeFilter, IsInactiveIncluded,
				cancellationToken);
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is null && _socket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientsAsync(cancellationToken);
		ClearState();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	private AmberdataRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private AmberdataSocketClient GetOrCreateSocket()
	{
		using (_sync.EnterScope())
		{
			if (_socket is not null)
				return _socket;
			var responseTimeout = ReConnectionSettings.TimeOutInterval;
			if (responseTimeout < TimeSpan.FromSeconds(1))
				responseTimeout = TimeSpan.FromSeconds(1);
			if (responseTimeout > TimeSpan.FromMinutes(5))
				responseTimeout = TimeSpan.FromMinutes(5);
			var socket = new AmberdataSocketClient(SocketEndpoint, Token,
				ReConnectionSettings.WorkingTime,
				Math.Max(1, ReConnectionSettings.ReAttemptCount), responseTimeout)
			{
				Parent = this,
			};
			socket.MessageReceived += OnSocketMessageAsync;
			socket.Error += SendOutErrorAsync;
			_socket = socket;
			return socket;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		AmberdataSocketClient socket;
		using (_sync.EnterScope())
		{
			socket = _socket;
			_socket = null;
		}
		if (socket is not null)
		{
			socket.MessageReceived -= OnSocketMessageAsync;
			socket.Error -= SendOutErrorAsync;
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			finally
			{
				socket.Dispose();
			}
		}
		_rest?.Dispose();
		_rest = null;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_references.Clear();
			_liveSubscriptions.Clear();
			_books.Clear();
		}
	}

	private static void ValidateSocketEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new InvalidOperationException(
				"Amberdata WebSocket endpoint must be an absolute WSS URI without query or fragment.");
	}
}
