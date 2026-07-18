namespace StockSharp.Bmll;

public partial class BmllMessageAdapter
{
	private BmllClient _client;
	private HashSet<string> _datasets = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance.</summary>
	public BmllMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(5);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BmllExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ValidateSettings();

		var client = new BmllClient(AuthenticationMode, Address, AuthenticationAddress,
			Token?.UnSecure(), ApiKey?.UnSecure(), Login, PrivateKeyPath,
			Password?.UnSecure(), Math.Max(1, ReConnectionSettings.ReAttemptCount),
			QueryPollingInterval, QueryTimeout)
		{
			Parent = this,
		};
		_client = client;
		try
		{
			var datasets = await client.GetDatasets(cancellationToken);
			_datasets = (datasets ?? []).Where(dataset =>
				dataset != null && !dataset.Name.IsEmpty())
				.Select(dataset => dataset.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			client.Dispose();
			_client = null;
			_datasets.Clear();
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

	private BmllClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
		_datasets.Clear();
	}

	private void ValidateDatasetAccess(string dataset)
	{
		dataset.ThrowIfEmpty(nameof(dataset));
		if (_datasets.Count > 0 && !_datasets.Contains(dataset))
		{
			throw new InvalidOperationException(
				$"BMLL dataset '{dataset}' is not available for this account. " +
				"Use an identifier returned by the BMLL datasets endpoint.");
		}
	}

	private void ValidateSettings()
	{
		ValidateAddress(Address, nameof(Address));
		ValidateAddress(AuthenticationAddress, nameof(AuthenticationAddress));
		TradesDataset.ThrowIfEmpty(nameof(TradesDataset));
		Level3Dataset.ThrowIfEmpty(nameof(Level3Dataset));
		if (QueryPollingInterval < TimeSpan.FromMilliseconds(100))
		{
			throw new ArgumentOutOfRangeException(nameof(QueryPollingInterval),
				QueryPollingInterval, "BMLL query polling interval is too short.");
		}
		if (QueryTimeout <= QueryPollingInterval)
		{
			throw new ArgumentOutOfRangeException(nameof(QueryTimeout), QueryTimeout,
				"BMLL query timeout must exceed the polling interval.");
		}
		if (MaxRecords <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxRecords), MaxRecords, null);
		if (MaxDepth <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxDepth), MaxDepth, null);
		if (DefaultHistoryDays <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(DefaultHistoryDays),
				DefaultHistoryDays, null);
		}

		switch (AuthenticationMode)
		{
			case BmllAuthenticationModes.SshKey:
				if (Login.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
				PrivateKeyPath.ThrowIfEmpty(nameof(PrivateKeyPath));
				if (!File.Exists(PrivateKeyPath))
					throw new FileNotFoundException("BMLL private key was not found.", PrivateKeyPath);
				break;
			case BmllAuthenticationModes.BearerToken:
				if (Token.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(AuthenticationMode),
					AuthenticationMode, null);
		}
	}

	private static void ValidateAddress(Uri address, string name)
	{
		if (address == null || !address.IsAbsoluteUri ||
			address.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException(
				$"BMLL {name} must be an absolute HTTPS URI.");
		}
	}
}
