namespace StockSharp.Ligther;

public partial class LigtherMessageAdapter
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
		try
		{
			foreach (var adapter in NativeAdapters.Values)
				await adapter.ConnectAsync(connectMsg, cancellationToken);

			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			ClearAdapters();
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);

		foreach (var adapter in NativeAdapters.Values)
			await adapter.DisconnectAsync(disconnectMsg, cancellationToken);

		ClearAdapters();
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

		ClearAdapters();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> NativeAdapters.Values.Select(a => a.TimeAsync(timeMsg, cancellationToken)).WhenAll();

	private void EnsureConnected()
	{
		if (NativeAdapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}
}
