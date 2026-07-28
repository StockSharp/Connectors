namespace StockSharp.OpenFigi;

public partial class OpenFigiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		_restClient = new(RestEndpoint, Key, RequestInterval)
		{
			Parent = this,
		};
		try
		{
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			_restClient.Dispose();
			_restClient = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
		_restClient.Dispose();
		_restClient = null;
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		_restClient?.Dispose();
		_restClient = null;
		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
