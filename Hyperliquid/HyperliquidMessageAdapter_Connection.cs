namespace StockSharp.Hyperliquid;

public partial class HyperliquidMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (NativeAdapters.Count > 0)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		RecreateAdapters();

		if (NativeAdapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		SuppressNativeConnectionState(true);

		try
		{
			foreach (var adapter in NativeAdapters.Values)
				await adapter.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			_nativeAdapters.Clear();
			ClearRoutingCaches();
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
		finally
		{
			SuppressNativeConnectionState(false);
		}

		await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		SuppressNativeConnectionState(true);

		try
		{
			foreach (var adapter in NativeAdapters.Values)
				await adapter.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			SuppressNativeConnectionState(false);
		}

		_nativeAdapters.Clear();
		ClearRoutingCaches();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		foreach (var adapter in NativeAdapters.Values)
		{
			try
			{
				await adapter.ResetAsync(resetMsg, cancellationToken);
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		_nativeAdapters.Clear();
		ClearRoutingCaches();

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> NativeAdapters.Values.Select(a => a.TimeAsync(timeMsg, cancellationToken)).WhenAll();
}
