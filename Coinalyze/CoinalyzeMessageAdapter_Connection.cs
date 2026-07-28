namespace StockSharp.Coinalyze;

public partial class CoinalyzeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		RestEndpoint = NormalizeEndpoint(RestEndpoint);
		Exchange = Exchange?.Trim();
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint, Token, RequestInterval)
			{
				Parent = this,
			};
			RememberInstruments(
				await RestClient.GetMarketsAsync(
					MarketType, cancellationToken));
			await SendOutConnectionStateAsync(
				ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			_restClient?.Dispose();
			_restClient = null;
			await SendOutConnectionStateAsync(
				ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting, cancellationToken);
		_restClient.Dispose();
		_restClient = null;
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
