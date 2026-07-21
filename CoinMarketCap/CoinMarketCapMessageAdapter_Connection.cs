namespace StockSharp.CoinMarketCap;

public partial class CoinMarketCapMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _socket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (!Enum.IsDefined(AccessMode))
			throw new InvalidOperationException(
				$"Unsupported CoinMarketCap access mode '{AccessMode}'.");
		if (AccessMode == CoinMarketCapAccessModes.ApiKey && Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		QuoteCurrency = CoinMarketCapExtensions.NormalizeCurrency(QuoteCurrency);
		ValidateSocketEndpoint(SocketEndpoint);
		_rest = new(ApiEndpoint, AccessMode, Token, RequestInterval)
		{
			Parent = this,
		};
		try
		{
			await _rest.ValidateAsync(cancellationToken);
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

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage message,
		CancellationToken cancellationToken)
	{
		CoinMarketCapSocketClient socket = null;
		using (_sync.EnterScope())
		{
			if (_socket is not null && CurrentTime.EnsureUtc() >= _nextPing)
			{
				socket = _socket;
				_nextPing = CurrentTime.EnsureUtc() + socket.PingInterval;
			}
		}
		if (socket is not null)
		{
			try
			{
				await socket.PingAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
		await base.TimeAsync(message, cancellationToken);
	}

	private CoinMarketCapRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinMarketCapSocketClient GetOrCreateSocket()
	{
		if (!IsStreamingEnabled)
			throw new NotSupportedException(
				"CoinMarketCap streaming is disabled in the adapter settings.");
		if (AccessMode != CoinMarketCapAccessModes.ApiKey || Token.IsEmpty())
			throw new NotSupportedException(
				"CoinMarketCap WebSocket requires an API key and Startup plan or above.");
		using (_sync.EnterScope())
		{
			if (_socket is not null)
				return _socket;
			var socket = new CoinMarketCapSocketClient(SocketEndpoint, Token,
				ReConnectionSettings.WorkingTime,
				Math.Max(1, ReConnectionSettings.ReAttemptCount))
			{
				Parent = this,
			};
			socket.PriceReceived += OnPriceAsync;
			socket.Error += SendOutErrorAsync;
			_socket = socket;
			_nextPing = CurrentTime.EnsureUtc() + socket.PingInterval;
			return socket;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		CoinMarketCapSocketClient socket;
		using (_sync.EnterScope())
		{
			socket = _socket;
			_socket = null;
			_nextPing = default;
		}
		if (socket is not null)
		{
			socket.PriceReceived -= OnPriceAsync;
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
			_coins.Clear();
			_liveSubscriptions.Clear();
		}
	}

	private static void ValidateSocketEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new InvalidOperationException(
				"CoinMarketCap WebSocket endpoint must be an absolute WSS URI without query or fragment.");
	}
}
