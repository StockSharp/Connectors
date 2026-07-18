namespace StockSharp.Toobit;

public partial class ToobitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
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

		foreach (var section in Sections.Distinct())
		{
			var adapter = section switch
			{
				ToobitSections.Spot => new ToobitSectionAdapter(Key, Secret, false,
					RestEndpoint, WsEndpoint, ReConnectionSettings.WorkingTime),
				ToobitSections.Futures => new ToobitSectionAdapter(Key, Secret, true,
					RestEndpoint, WsEndpoint, ReConnectionSettings.WorkingTime),
				_ => throw new ArgumentOutOfRangeException(nameof(section), section, LocalizedStrings.InvalidValue),
			};

			adapter.Parent = this;
			adapter.NewOutMessage += SendOutMessageAsync;
			_adapters.Add(adapter.BoardCode, adapter);
		}

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await _adapters.CachedValues.Select(a => a.ConnectAsync(cancellationToken)).WhenAll();
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeAdaptersAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisposeAdaptersAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await DisposeAdaptersAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _adapters.CachedValues.Select(a => a.TimeAsync(timeMsg, cancellationToken)).WhenAll();

	private async ValueTask DisposeAdaptersAsync(CancellationToken cancellationToken)
	{
		foreach (var (_, adapter) in _adapters.CopyAndClear())
		{
			try
			{
				adapter.Disconnect();
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				adapter.NewOutMessage -= SendOutMessageAsync;
				adapter.Dispose();
			}
		}
	}
}
