namespace StockSharp.CoinGecko;

public partial class CoinGeckoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _socket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		QuoteCurrency = CoinGeckoExtensions.NormalizeCurrency(QuoteCurrency);
		ValidateSocketEndpoint(SocketEndpoint);
		_rest = new(ApiEndpoint, Tier, Token, RequestInterval)
		{
			Parent = this,
		};
		try
		{
			await _rest.ValidateAsync(cancellationToken);
			var currencies = await _rest.GetSupportedCurrenciesAsync(
				cancellationToken);
			if (!currencies.Contains(QuoteCurrency,
				StringComparer.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					$"CoinGecko does not support quote currency '{QuoteCurrency}'.");
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

	private CoinGeckoRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private CoinGeckoSocketClient GetOrCreateSocket()
	{
		if (!IsStreamingEnabled)
			throw new NotSupportedException(
				"CoinGecko streaming is disabled in the adapter settings.");
		if (Tier != CoinGeckoApiTiers.Pro)
			throw new NotSupportedException(
				"CoinGecko WebSocket requires a Pro API key and Analyst plan or above.");
		using (_sync.EnterScope())
		{
			if (_socket is not null)
				return _socket;
			var socket = new CoinGeckoSocketClient(SocketEndpoint, Token,
				ReConnectionSettings.WorkingTime,
				Math.Max(1, ReConnectionSettings.ReAttemptCount))
			{
				Parent = this,
			};
			socket.CoinPriceReceived += OnCoinPriceAsync;
			socket.OnchainPriceReceived += OnOnchainPriceAsync;
			socket.TradeReceived += OnOnchainTradeAsync;
			socket.OhlcvReceived += OnOnchainOhlcvAsync;
			socket.Error += SendOutErrorAsync;
			_socket = socket;
			return socket;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		CoinGeckoSocketClient socket;
		using (_sync.EnterScope())
		{
			socket = _socket;
			_socket = null;
		}
		if (socket is not null)
		{
			socket.CoinPriceReceived -= OnCoinPriceAsync;
			socket.OnchainPriceReceived -= OnOnchainPriceAsync;
			socket.TradeReceived -= OnOnchainTradeAsync;
			socket.OhlcvReceived -= OnOnchainOhlcvAsync;
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
			_pools.Clear();
			_liveSubscriptions.Clear();
			_seenTradeIds.Clear();
			_seenTradeOrder.Clear();
		}
	}

	private static void ValidateSocketEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new InvalidOperationException(
				"CoinGecko WebSocket endpoint must be an absolute WSS URI without query or fragment.");
	}
}
