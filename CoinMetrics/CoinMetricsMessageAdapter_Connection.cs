namespace StockSharp.CoinMetrics;

public partial class CoinMetricsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ExchangeFilter = NormalizeOptionalFilter(ExchangeFilter);
		ValidateSocketEndpoint(SocketEndpoint);
		_rest = new(ApiEndpoint, Token, RequestInterval)
		{
			Parent = this,
		};
		try
		{
			await _rest.ValidateAsync(ExchangeFilter, cancellationToken);
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
		if (_rest is null)
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

	private CoinMetricsRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		Exception firstError = null;
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			CoinMetricsStreamClient[] streams;
			using (_sync.EnterScope())
			{
				streams = [.. _streams.Values];
				_streams.Clear();
				_liveSubscriptions.Clear();
			}
			foreach (var stream in streams)
			{
				stream.MessageReceived -= OnStreamMessageAsync;
				stream.Error -= SendOutErrorAsync;
				try
				{
					await stream.DisconnectAsync(cancellationToken);
				}
				catch (Exception error)
				{
					firstError ??= error;
				}
				finally
				{
					stream.Dispose();
				}
			}
		}
		finally
		{
			_streamGate.Release();
		}
		_rest?.Dispose();
		_rest = null;
		if (firstError is not null)
			throw firstError;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_liveSubscriptions.Clear();
			_streams.Clear();
		}
	}

	private static string NormalizeOptionalFilter(string value)
	{
		if (value.IsEmpty())
			return null;
		value = value.Trim();
		if (value.Length > 128 || value.Any(character =>
			char.IsControl(character) || character is '?' or '#' or '&' or ','))
			throw new ArgumentException(
				"Coin Metrics exchange filter contains unsupported characters.",
				nameof(value));
		return value;
	}

	private static void ValidateSocketEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new InvalidOperationException(
				"Coin Metrics WebSocket endpoint must be an absolute WSS URI without credentials, query, or fragment.");
	}
}
