namespace StockSharp.SpGlobal;

public partial class SpGlobalMessageAdapter
{
	private const string _boardCode = "SPGCI";

	private SpGlobalClient _client;

	/// <summary>Initializes a new instance.</summary>
	public SpGlobalMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Level1;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [_boardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Login.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
		if (Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri)
			throw new InvalidOperationException("S&P Global API address must be absolute.");

		var client = new SpGlobalClient(Address, Login, Password.UnSecure())
		{
			Parent = this,
		};
		_client = client;
		try
		{
			await client.Authenticate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			client.Dispose();
			_client = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
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
		_client?.Dispose();
		_client = null;
	}

	private SpGlobalClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
}
