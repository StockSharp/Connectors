namespace StockSharp.Comdirect;

public partial class ComdirectMessageAdapter
{
    private readonly SynchronizedDictionary<string, ComdirectDepot>
        _depotsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, ComdirectDepot>
        _depotsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, ComdirectInstrument>
        _instruments = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenExecutions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private ComdirectRestClient _rest;
    private long _portfolioSubscriptionId;
    private string _portfolioNameFilter;
    private long _orderStatusSubscriptionId;
    private OrderStatusMessage _orderStatusFilter;
    private DateTime _lastPoll;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ComdirectMessageAdapter"/> class.
    /// </summary>
    /// <param name="transactionIdGenerator">
    /// Transaction identifier generator.
    /// </param>
    public ComdirectMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMessage(MessageTypes.SecurityLookup, true);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.PositionChanges ||
            dataType == DataType.Transactions ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (PollingInterval < TimeSpan.FromSeconds(1))
            throw new InvalidOperationException(
                "comdirect polling interval must be at least one second.");
        if (DefaultCurrency.IsEmpty() || DefaultCurrency.Length != 3)
            throw new InvalidOperationException(
                "comdirect default currency must be a three-letter ISO code.");

        _rest = new(Address, Key, Secret, Login, Password, TanType,
            TanProvider, Math.Max(1, ReConnectionSettings.ReAttemptCount))
        {
            Parent = this,
        };

        try
        {
            await _rest.Authenticate(cancellationToken);
            await RefreshDepots(cancellationToken);
            _lastPoll = DateTime.UtcNow;
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
        DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
    {
        if (_rest is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);

        try
        {
            try
            {
                await _rest.Revoke(cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                this.AddWarningLog(
                    "comdirect token revoke failed: {0}", error.Message);
            }
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClient();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        _depotsByName.Clear();
        _depotsById.Clear();
        _instruments.Clear();
        _orderTransactions.Clear();
        _trackedOrders.Clear();
        _seenExecutions.Clear();
        _orderFingerprints.Clear();
        _portfolioSubscriptionId = 0;
        _portfolioNameFilter = null;
        _orderStatusSubscriptionId = 0;
        _orderStatusFilter = null;
        _lastPoll = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_rest is not null && now - _lastPoll >= PollingInterval)
        {
            _lastPoll = now;
            try
            {
                if (_portfolioSubscriptionId != 0)
                {
                    await SendPortfolioSnapshot(
                        _portfolioSubscriptionId, _portfolioNameFilter,
                        cancellationToken);
                }

                if (_orderStatusSubscriptionId != 0)
                {
                    await SendOrderSnapshot(
                        _orderStatusSubscriptionId,
                        _orderStatusFilter, cancellationToken);
                }
                else
                    await PollTrackedOrders(cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private async Task RefreshDepots(CancellationToken cancellationToken)
    {
        var depots = await Rest.GetDepots(cancellationToken);
        _depotsByName.Clear();
        _depotsById.Clear();
        foreach (var depot in depots)
        {
            if (depot?.DepotId.IsEmpty() != false)
                continue;
            _depotsById[depot.DepotId] = depot;
            _depotsByName[GetPortfolioName(depot)] = depot;
        }
    }

    private ComdirectDepot ResolveDepot(string portfolioName)
    {
        if (!portfolioName.IsEmpty())
        {
            if (_depotsByName.TryGetValue(portfolioName, out var byName))
                return byName;
            if (_depotsById.TryGetValue(portfolioName, out var byId))
                return byId;
            throw new InvalidOperationException(
                $"Unknown comdirect depot '{portfolioName}'.");
        }

        return _depotsByName.Values.FirstOrDefault() ??
            throw new InvalidOperationException(
                "No comdirect depot is available.");
    }

    private ComdirectDepot[] ResolveDepots(string portfolioName)
        => portfolioName.IsEmpty()
            ? _depotsById.Values.ToArray()
            : [ResolveDepot(portfolioName)];

    private async Task<ComdirectInstrument> GetInstrument(string id,
        CancellationToken cancellationToken)
    {
        if (id.IsEmpty())
            return null;
        if (_instruments.TryGetValue(id, out var cached))
            return cached;

        var instrument = await Rest.GetInstrument(id, cancellationToken);
        CacheInstrument(instrument, id);
        return instrument;
    }

    private void CacheInstrument(ComdirectInstrument instrument,
        params string[] aliases)
    {
        if (instrument is null)
            return;
        foreach (var alias in aliases.Append(instrument.InstrumentId)
            .Append(instrument.Wkn).Append(instrument.Isin)
            .Append(instrument.Mnemonic).Where(a => !a.IsEmpty()))
            _instruments[alias] = instrument;
    }

    private async Task PollTrackedOrders(
        CancellationToken cancellationToken)
    {
        if (_trackedOrders.Count == 0)
            return;

        foreach (var orderId in _trackedOrders.ToArray())
        {
            try
            {
                var order = await Rest.GetOrder(
                    orderId, cancellationToken);
                await ProcessOrder(order, 0, cancellationToken);
            }
            catch (HttpRequestException error) when (
                error.StatusCode == HttpStatusCode.NotFound)
            {
                // Newly created orders can briefly be absent from the book.
            }
        }
    }

    private ComdirectRestClient Rest => _rest ??
        throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private static string GetPortfolioName(ComdirectDepot depot)
        => depot.DepotDisplayId.IsEmpty(depot.DepotId);

    private void DisposeClient()
    {
        _rest?.Dispose();
        _rest = null;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClient();
        base.DisposeManaged();
    }
}
