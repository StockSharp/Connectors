namespace StockSharp.EdgeX;

public partial class EdgeXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		_ = connectMsg;

		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		if (_adapters.Count > 0)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		RecreateAdapters();

		if (_adapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await _adapters.CachedValues.Select(a => a.ConnectAsync(cancellationToken)).WhenAll();
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisconnectAndDisposeAdaptersAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;

		EnsureConnected();

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisconnectAndDisposeAdaptersAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_ = resetMsg;

		await ResetAndDisposeAdaptersAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _adapters.CachedValues.Select(a => a.TimeAsync(timeMsg, cancellationToken)).WhenAll();

	private async ValueTask DisconnectAndDisposeAdaptersAsync(CancellationToken cancellationToken)
	{
		foreach (var (_, adapter) in _adapters.CopyAndClear())
		{
			try
			{
				adapter.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
			finally
			{
				adapter.NewOutMessage -= SendOutMessageAsync;
				adapter.Dispose();
			}
		}
	}

	private async ValueTask ResetAndDisposeAdaptersAsync(CancellationToken cancellationToken)
	{
		foreach (var (_, adapter) in _adapters.CopyAndClear())
		{
			try
			{
				await adapter.ResetAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
			finally
			{
				adapter.NewOutMessage -= SendOutMessageAsync;
				adapter.Dispose();
			}
		}
	}
}
