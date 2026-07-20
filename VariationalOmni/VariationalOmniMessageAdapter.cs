namespace StockSharp.VariationalOmni;

public partial class VariationalOmniMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, VariationalOmniListing> _listings =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _level1Subscriptions = [];
	private VariationalOmniRestClient _client;
	private VariationalOmniStatistics _statistics;
	private DateTime _nextRefreshTime;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="VariationalOmniMessageAdapter"/>.
	/// </summary>
	public VariationalOmniMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.VariationalOmni];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.VariationalOmni) ||
			securityId.IsAssociated(BoardCodes.VariationalOmni);

	private VariationalOmniRestClient Client => _client ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_client = new(Endpoint) { Parent = this };
			await RefreshStatisticsAsync(cancellationToken);
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
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		_ = Client;
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		DisposeClient();
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		KeyValuePair<long, string>[] subscriptions;
		using (_sync.EnterScope())
		{
			if (_client is null || _level1Subscriptions.Count == 0 ||
				now < _nextRefreshTime)
			{
				subscriptions = [];
			}
			else
			{
				_nextRefreshTime = now + PollingInterval;
				subscriptions = [.. _level1Subscriptions];
			}
		}

		if (subscriptions.Length > 0)
		{
			try
			{
				await RefreshStatisticsAsync(cancellationToken);
				foreach (var subscription in subscriptions)
					if (TryGetListing(subscription.Value, out var listing))
						await SendLevel1Async(listing, subscription.Key,
							cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshStatisticsAsync(
		CancellationToken cancellationToken)
	{
		var statistics = await Client.GetStatisticsAsync(cancellationToken);
		var listings = (statistics.Listings ?? [])
			.Where(static listing => listing?.Ticker.IsEmpty() == false)
			.GroupBy(static listing => listing.Ticker.Trim(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.Last())
			.ToArray();
		if (listings.Length == 0)
			throw new InvalidDataException(
				"Variational Omni returned no market listings.");

		using (_sync.EnterScope())
		{
			_statistics = statistics;
			_listings.Clear();
			foreach (var listing in listings)
				_listings[listing.Ticker.Trim()] = listing;
			_nextRefreshTime = DateTime.UtcNow + PollingInterval;
		}
	}

	private async ValueTask EnsureFreshStatisticsAsync(
		CancellationToken cancellationToken)
	{
		bool isRefreshRequired;
		using (_sync.EnterScope())
			isRefreshRequired = _statistics is null ||
				DateTime.UtcNow >= _nextRefreshTime;
		if (isRefreshRequired)
			await RefreshStatisticsAsync(cancellationToken);
	}

	private VariationalOmniListing GetListing(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Variational Omni.");
		var ticker = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		return TryGetListing(ticker, out var listing)
			? listing
			: throw new InvalidOperationException(
				$"Unknown Variational Omni market '{ticker}'.");
	}

	private bool TryGetListing(string ticker,
		out VariationalOmniListing listing)
	{
		using (_sync.EnterScope())
			return _listings.TryGetValue(ticker ?? string.Empty, out listing);
	}

	private VariationalOmniListing[] GetListings()
	{
		using (_sync.EnterScope())
			return [.. _listings.Values];
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_listings.Clear();
			_level1Subscriptions.Clear();
			_statistics = null;
			_nextRefreshTime = default;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
