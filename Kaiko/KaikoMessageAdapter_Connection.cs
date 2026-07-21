namespace StockSharp.Kaiko;

public partial class KaikoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _stream is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (!Enum.IsDefined(Region))
			throw new InvalidOperationException(
				$"Unsupported Kaiko region '{Region}'.");
		if (!Enum.IsDefined(InstrumentClassFilter))
			throw new InvalidOperationException(
				$"Unsupported Kaiko instrument class '{InstrumentClassFilter}'.");
		_rest = new(ReferenceEndpoint, MarketEndpoint, Token, RequestInterval)
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
		if (_rest is null && _stream is null)
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

	private KaikoRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private KaikoStreamClient GetOrCreateStream()
	{
		if (!IsStreamingEnabled)
			throw new NotSupportedException(
				"Kaiko streaming is disabled in the adapter settings.");
		if (Token.IsEmpty())
			throw new NotSupportedException(
				"Kaiko Stream requires an API key.");
		using (_sync.EnterScope())
		{
			if (_stream is not null)
				return _stream;
			var stream = new KaikoStreamClient(StreamEndpoint, Token,
				Math.Max(0, ReConnectionSettings.ReAttemptCount),
				OnMarketUpdateAsync, OnOhlcvUpdateAsync, SendOutErrorAsync)
			{
				Parent = this,
			};
			_stream = stream;
			return stream;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		KaikoStreamClient stream;
		using (_sync.EnterScope())
		{
			stream = _stream;
			_stream = null;
		}
		if (stream is not null)
		{
			try
			{
				await stream.DisconnectAsync(cancellationToken);
			}
			finally
			{
				stream.Dispose();
			}
		}
		_rest?.Dispose();
		_rest = null;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_liveSubscriptions.Clear();
		}
	}
}
