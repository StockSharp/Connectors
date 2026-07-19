namespace StockSharp.BitpandaFusion;

public partial class BitpandaFusionMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(
				"Bitpanda Fusion API key is not specified.");
		if (Address is null || !Address.IsAbsoluteUri ||
			!Address.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				"Bitpanda Fusion address must be an absolute HTTPS URI.");
		}

		ClearState();
		_restClient = new(Address.AbsoluteUri, Token) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_ = await RestClient.GetTimeAsync(cancellationToken);
			var pairs = await RestClient.GetPairsAsync(null, cancellationToken);
			var assets = await RestClient.GetAssetsAsync(null, cancellationToken);
			RegisterReferenceData(pairs, assets);
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			DisposeClient();
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		DisposeClient();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
