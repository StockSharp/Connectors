namespace StockSharp.Finra;

public partial class FinraMessageAdapter
{
	private FinraRestClient _client;

	/// <summary>Initializes a new instance.</summary>
	public FinraMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Level1 ||
			dataType == DataType.Securities;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[FinraExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Address is null || !Address.IsAbsoluteUri)
		{
			throw new InvalidOperationException(
				"FINRA Query API address must be absolute.");
		}
		if (AuthAddress is null || !AuthAddress.IsAbsoluteUri)
		{
			throw new InvalidOperationException(
				"FINRA OAuth address must be absolute.");
		}
		if (PageSize is < 1 or > 5000)
		{
			throw new InvalidOperationException(
				"FINRA page size must be from 1 to 5000.");
		}
		if (MaxRecords is < 1 or > 505000)
		{
			throw new InvalidOperationException(
				"FINRA maximum records must be from 1 to 505000.");
		}
		if (DataVersion < 1)
		{
			throw new InvalidOperationException(
				"FINRA data version must be positive.");
		}

		var token = Token.UnSecure();
		var key = Key.UnSecure();
		var secret = Secret.UnSecure();
		if (token.IsEmpty() && (key.IsEmpty() || secret.IsEmpty()))
		{
			throw new InvalidOperationException(
				"Specify a FINRA access token or both API Client ID and Client Secret.");
		}

		_client = new FinraRestClient(
			Address,
			AuthAddress,
			key,
			secret,
			token,
			DataVersion)
		{
			Parent = this,
		};

		try
		{
			await _client.EnsureAuthenticated(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);

		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private FinraRestClient SafeClient()
		=> _client ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}
}
