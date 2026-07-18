namespace StockSharp.Marketstack;

public partial class MarketstackMessageAdapter
{
	private MarketstackRestClient _rest;

	/// <summary>Initializes a new instance.</summary>
	public MarketstackMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [Extensions.BoardCode];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri ||
			!Address.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				"Marketstack address must be an absolute HTTPS URI.");
		}

		var rest = new MarketstackRestClient(Address, Token.UnSecure(),
			Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_rest = rest;
		try
		{
			await rest.Validate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			rest.Dispose();
			_rest = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeRest();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeRest();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private MarketstackRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void DisposeRest()
	{
		_rest?.Dispose();
		_rest = null;
	}
}
