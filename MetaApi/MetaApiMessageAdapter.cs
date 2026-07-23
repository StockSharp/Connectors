namespace StockSharp.MetaApi;

public partial class MetaApiMessageAdapter
{
	private enum MetaApiSubscriptionKind
	{
		Quotes,
		Ticks,
		MarketDepth,
		Candles,
	}

	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Symbol { get; init; }
		public MetaApiSubscriptionKind Kind { get; init; }
		public TimeSpan? TimeFrame { get; init; }
	}

	private readonly record struct OrderSignature(
		string State,
		decimal? OpenPrice,
		decimal? CurrentPrice,
		decimal Volume,
		decimal? CurrentVolume,
		decimal? StopLoss,
		decimal? TakeProfit,
		DateTime? DoneTime);

	private readonly record struct PositionSignature(
		decimal NetVolume,
		decimal AveragePrice,
		decimal CurrentPrice,
		decimal UnrealizedPnL,
		decimal RealizedPnL,
		decimal Commission);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MetaApiSymbolSpecification> _specifications =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MetaApiOrder> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MetaApiPosition> _positions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _transactionOrders = [];
	private readonly Dictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderSignature> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PositionSignature> _positionSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _reportedDeals =
		new(StringComparer.OrdinalIgnoreCase);
	private MetaApiRestClient _rest;
	private MetaApiStreamingClient _stream;
	private MetaApiAccountInformation _accountInformation;
	private bool _receivedOrders;
	private bool _receivedPositions;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="MetaApiMessageAdapter"/> class.</summary>
	public MetaApiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(3);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames([.. MetaApiExtensions.TimeFrames.Keys]);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || dataType == DataType.Ticks ||
			dataType.IsTFCandles || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [MetaApiExtensions.BoardCode];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(MetaApiExtensions.BoardCode) ||
			securityId.IsAssociated(MetaApiExtensions.BoardCode);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState(true);
		var accountId = AccountId.ThrowIfEmpty(nameof(AccountId));
		var domain = Domain.ThrowIfEmpty(nameof(Domain));
		var rest = new MetaApiRestClient(domain,
			Token ?? throw new InvalidOperationException("MetaApi access token is required."),
			Region, Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_rest = rest;
		try
		{
			var settings = await rest.GetServerSettingsAsync(cancellationToken);
			var region = Region;
			MetaApiAccount account = null;
			if (region.IsEmpty())
			{
				try
				{
					account = await rest.GetAccountAsync(accountId, cancellationToken);
					region = account?.Region;
				}
				catch (MetaApiApiException error) when (
					error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				{
					throw new InvalidOperationException(
						"MetaApi account access tokens require Region to be configured explicitly.",
						error);
				}
			}
			region = region.ThrowIfEmpty(nameof(Region));
			if (account?.State.IsEmpty() == false &&
				!account.State.EqualsIgnoreCase("DEPLOYED"))
			{
				throw new InvalidOperationException(
					$"MetaApi account '{accountId}' is in state '{account.State}', not DEPLOYED.");
			}
			if (account?.ConnectionStatus.EqualsIgnoreCase("DISCONNECTED_FROM_BROKER") == true)
			{
				throw new InvalidOperationException(
					$"MetaApi account '{accountId}' is disconnected from its broker.");
			}

			rest.SetRegion(region);
			var resolvedDomain = settings?.Domain.IsEmpty(domain);
			var hostname = settings?.Hostname.IsEmpty("mt-client-api-v1");
			var server = new Uri($"https://{hostname}.{region}-a.{resolvedDomain}");
			var stream = new MetaApiStreamingClient(server, Token, accountId,
				Math.Max(1, ReConnectionSettings.ReAttemptCount),
				ReConnectionSettings.WorkingTime)
			{
				Parent = this,
			};
			_stream = stream;
			stream.PacketReceived += ProcessPacketAsync;
			stream.SynchronizationStarted += ProcessSynchronizationStartedAsync;
			stream.Synchronized += ProcessSynchronizedAsync;
			stream.Error += SendOutErrorAsync;
			stream.StateChanged += SendOutConnectionStateAsync;
			await stream.ConnectAndSynchronizeAsync(SynchronizationTimeout,
				cancellationToken);
			await LoadMissingSnapshotPartsAsync(cancellationToken);

			connectMsg.SessionId = $"MetaApi {accountId}/{region}";
			this.AddInfoLog(
				"Connected to MetaApi account {0} in {1}; cached {2} symbol specifications.",
				accountId, region, GetSpecifications().Length);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(default);
			ClearState(true);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientsAsync(cancellationToken);
		ClearState(true);
		this.AddInfoLog("MetaApi disconnected without changing the deployed account.");
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		ClearState(true);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		if (_stream is not null)
			await _stream.PingAsync(cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask ProcessPacketAsync(MetaApiSynchronizationPacket packet,
		CancellationToken cancellationToken)
	{
		switch (packet.Type)
		{
			case "accountInformation":
				await UpdateAccountInformationAsync(packet.AccountInformation,
					cancellationToken);
				break;
			case "specifications":
				ProcessSpecifications(packet);
				break;
			case "orders":
				await ReplaceOrdersAsync(packet.Orders ?? [],
					cancellationToken);
				break;
			case "positions":
				await ReplacePositionsAsync(packet.Positions ?? [],
					cancellationToken);
				break;
			case "deals":
				await ProcessDealsAsync(packet.Deals ?? [],
					cancellationToken);
				break;
			case "update":
				await ProcessTerminalUpdateAsync(packet, cancellationToken);
				break;
			case "prices":
				await ProcessPricesPacketAsync(packet, cancellationToken);
				break;
			case "status":
				if (packet.Connected == false)
					this.AddDebugLog("MetaApi reported a disconnected terminal status.");
				break;
		}
	}

	private ValueTask ProcessSynchronizationStartedAsync(
		CancellationToken cancellationToken)
	{
		ClearSnapshot();
		return default;
	}

	private ValueTask ProcessSynchronizedAsync(CancellationToken cancellationToken)
	{
		this.AddDebugLog("MetaApi synchronized snapshot accepted for account {0}.",
			AccountId);
		return default;
	}

	private async Task LoadMissingSnapshotPartsAsync(CancellationToken cancellationToken)
	{
		bool loadOrders;
		bool loadPositions;
		bool loadAccount;
		using (_sync.EnterScope())
		{
			loadOrders = !_receivedOrders;
			loadPositions = !_receivedPositions;
			loadAccount = _accountInformation is null;
		}
		if (loadOrders)
			await ReplaceOrdersAsync(await Rest.GetOrdersAsync(AccountId,
				cancellationToken) ?? [], cancellationToken);
		if (loadPositions)
			await ReplacePositionsAsync(await Rest.GetPositionsAsync(AccountId,
				cancellationToken) ?? [], cancellationToken);
		if (loadAccount)
			await UpdateAccountInformationAsync(await Rest.GetAccountInformationAsync(
				AccountId, cancellationToken), cancellationToken);
	}

	private void ProcessSpecifications(MetaApiSynchronizationPacket packet)
	{
		var specifications = packet.Specifications ?? [];
		var removed = packet.RemovedSymbols ?? [];
		using (_sync.EnterScope())
		{
			foreach (var specification in specifications)
			{
				if (specification?.Symbol.IsEmpty() == false)
					_specifications[specification.Symbol] = specification;
			}
			foreach (var symbol in removed)
				_specifications.Remove(symbol);
		}
	}

	private MetaApiSymbolSpecification[] GetSpecifications()
	{
		using (_sync.EnterScope())
			return [.. _specifications.Values];
	}

	private MetaApiRestClient Rest
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private MetaApiStreamingClient Stream
		=> _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private string PortfolioName => AccountId.ThrowIfEmpty(nameof(AccountId));

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"MetaApi session is connected to account '{PortfolioName}', not '{portfolioName}'.");
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var stream = _stream;
		var rest = _rest;
		_stream = null;
		_rest = null;
		try
		{
			if (stream is not null)
			{
				stream.PacketReceived -= ProcessPacketAsync;
				stream.SynchronizationStarted -= ProcessSynchronizationStartedAsync;
				stream.Synchronized -= ProcessSynchronizedAsync;
				stream.Error -= SendOutErrorAsync;
				stream.StateChanged -= SendOutConnectionStateAsync;
				await stream.DisconnectAsync(cancellationToken);
			}
		}
		finally
		{
			stream?.Dispose();
			rest?.Dispose();
		}
	}

	private void ClearSnapshot()
	{
		using (_sync.EnterScope())
		{
			_specifications.Clear();
			_orders.Clear();
			_positions.Clear();
			_accountInformation = null;
			_receivedOrders = false;
			_receivedPositions = false;
		}
	}

	private void ClearState(bool isFull)
	{
		ClearSnapshot();
		if (!isFull)
			return;
		using (_sync.EnterScope())
		{
			_marketSubscriptions.Clear();
			_orderTransactions.Clear();
			_transactionOrders.Clear();
			_cancelTransactions.Clear();
			_orderSignatures.Clear();
			_positionSignatures.Clear();
			_reportedDeals.Clear();
			_orderStatusSubscriptionId = 0;
			_portfolioSubscriptionId = 0;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
