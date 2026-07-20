namespace StockSharp.CowProtocol;

public partial class CowProtocolMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rpcClient is not null || _httpClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (!System.Enum.IsDefined(Chain))
            throw new InvalidOperationException(
                $"Unsupported CoW Protocol chain '{Chain}'.");
        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _rpcClient = new(RpcEndpoint.IsEmpty()
                    ? Chain.GetRpcEndpoint()
                    : RpcEndpoint,
                Chain, WalletAddress, PrivateKey)
            {
                Parent = this,
            };
            _httpClient = new(ApiEndpoint.IsEmpty()
                    ? Chain.GetApiEndpoint()
                    : ApiEndpoint)
            {
                Parent = this,
            };
            await RpcClient.VerifyAsync(cancellationToken);
            await HttpClient.VerifyAsync(cancellationToken);
            WalletAddress = RpcClient.IsWalletConfigured
                ? RpcClient.WalletAddress
                : null;
            _ = await GetOrLoadTokenAsync(
                CowProtocolExtensions.NativeTokenAddress, cancellationToken);

            CowProtocolMarketDefinition[] definitions;
            try
            {
                definitions = ParseMarketDefinitions();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "Configured CoW Protocol market definitions are invalid.",
                    error);
            }
            var errors = new List<Exception>();
            foreach (var definition in definitions)
            {
                try
                {
                    await RegisterDefinitionAsync(definition,
                        cancellationToken);
                }
                catch (Exception error) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    errors.Add(error);
                    this.AddWarningLog(
                        "CoW Protocol market {0}/{1} loading failed: {2}",
                        definition.BaseToken, definition.QuoteToken,
                        error.Message);
                }
            }
            CowProtocolMarket[] markets;
            using (_sync.EnterScope())
                markets = [.. _markets.Values];
            if (markets.Length == 0)
                throw errors.Count == 1
                    ? errors[0]
                    : new AggregateException(
                        "No CoW Protocol markets could be loaded.", errors);

            connectMsg.SessionId = RpcClient.IsWalletConfigured
                ? $"CoW Protocol {Chain} {RpcClient.WalletAddress}"
                : $"CoW Protocol {Chain} public";
            await SendOutConnectionStateAsync(ConnectionStates.Connected,
                cancellationToken);
        }
        catch
        {
            DisposeClients();
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
        EnsureConnected();
        await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
            cancellationToken);
        DisposeClients();
        await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClients();
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var pollMarket = false;
        var pollPrivate = false;
        using (_sync.EnterScope())
        {
            var hasMarketSubscriptions =
                _level1Subscriptions.Count > 0 ||
                _tickSubscriptions.Count > 0 ||
                _candleSubscriptions.Count > 0;
            if (_rpcClient is not null && _httpClient is not null &&
                hasMarketSubscriptions && now >= _nextMarketPoll)
            {
                _nextMarketPoll = now + PollingInterval;
                pollMarket = true;
            }
            if (_rpcClient is not null && _httpClient is not null &&
                (_portfolioSubscriptions.Count > 0 ||
                    _orderSubscriptions.Count > 0 ||
                    _trackedOrders.Values.Any(static order =>
                        ToOrderState(order.Order.Status) ==
                        OrderStates.Active)) && now >= _nextPrivatePoll)
            {
                _nextPrivatePoll = now + PollingInterval;
                pollPrivate = true;
            }
        }
        if (pollMarket)
            await RunSafelyAsync(PollMarketAsync, cancellationToken);
        if (pollPrivate)
            await RunSafelyAsync(PollPrivateAsync, cancellationToken);
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private async ValueTask RegisterDefinitionAsync(
        CowProtocolMarketDefinition definition,
        CancellationToken cancellationToken)
    {
        var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
            cancellationToken);
        var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
            cancellationToken);
        if (baseToken.Address.EqualsIgnoreCase(quoteToken.Address))
            throw new InvalidDataException(
                "CoW Protocol market tokens must be different.");
        if (baseToken.Address.IsNativeToken() ||
            quoteToken.Address.IsNativeToken())
            throw new NotSupportedException(
                "CoW Protocol orders require ERC-20 tokens; configure wrapped " +
                "native tokens.");
        var code = definition.SecurityCode.IsEmpty()
            ? NormalizeSecurityCode(
                $"{baseToken.Symbol}-{quoteToken.Symbol}")
            : NormalizeSecurityCode(definition.SecurityCode);
        var market = new CowProtocolMarket
        {
            BaseToken = baseToken,
            QuoteToken = quoteToken,
            SecurityCode = code,
        };
        var probe = ProbeVolume.ToBaseUnits(baseToken.Decimals);
        if (probe <= 0)
            throw new InvalidOperationException(
                "The configured CoW Protocol quote probe volume rounds to zero.");
        _ = await GetQuoteAsync(market, CowProtocolTradeTypes.ExactInput,
            probe, cancellationToken);
        _ = await GetQuoteAsync(market, CowProtocolTradeTypes.ExactOutput,
            probe, cancellationToken);
        RegisterMarket(market);
    }

    private async ValueTask<CowProtocolToken> GetOrLoadTokenAsync(
        string address, CancellationToken cancellationToken)
    {
        address = address.NormalizeAddress();
        using (_sync.EnterScope())
            if (_tokens.TryGetValue(address, out var cached))
                return cached;
        var token = await RpcClient.GetTokenAsync(address,
            cancellationToken);
        using (_sync.EnterScope())
        {
            _tokens[token.Address] = token;
            return token;
        }
    }

    private void RegisterMarket(CowProtocolMarket market)
    {
        if (market?.BaseToken is null || market.QuoteToken is null ||
            market.SecurityCode.IsEmpty())
            throw new InvalidDataException(
                "CoW Protocol market metadata is incomplete.");
        var pair = CreatePairKey(market.BaseToken.Address,
            market.QuoteToken.Address);
        using (_sync.EnterScope())
        {
            if (_markets.ContainsKey(market.SecurityCode))
                throw new InvalidOperationException(
                    $"CoW Protocol security code '{market.SecurityCode}' is " +
                    "configured twice.");
            if (_marketsByPair.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"CoW Protocol pair '{pair}' is configured twice.");
            _tokens[market.BaseToken.Address] = market.BaseToken;
            _tokens[market.QuoteToken.Address] = market.QuoteToken;
            _markets.Add(market.SecurityCode, market);
            _marketsByPair.Add(pair, market);
        }
    }

    private CowProtocolMarketDefinition[] ParseMarketDefinitions()
    {
        var configured = Markets.IsEmpty()
            ? Chain.GetDefaultMarkets()
            : Markets;
        var result = new List<CowProtocolMarketDefinition>();
        foreach (var item in configured.Split(';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            var fields = item.Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length is not 2 and not 3)
                throw new FormatException(
                    "Each CoW Protocol market must use " +
                    "base-token|quote-token|security-code format; the security " +
                    "code is optional.");
            result.Add(new()
            {
                BaseToken = fields[0].NormalizeAddress(),
                QuoteToken = fields[1].NormalizeAddress(),
                SecurityCode = fields.Length == 3
                    ? NormalizeSecurityCode(fields[2])
                    : null,
            });
        }
        if (result.Count == 0)
            throw new InvalidOperationException(
                "At least one CoW Protocol market must be configured.");
        return [.. result];
    }

    private static string NormalizeSecurityCode(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
        if (value.Length > 64 || value.Any(static ch =>
            !char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
            throw new FormatException(
                $"Invalid CoW Protocol security code '{value}'.");
        return value;
    }

    private async ValueTask RunSafelyAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }
    }

    private void DisposeClients()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _rpcClient?.Dispose();
        _rpcClient = null;
        ClearState();
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _marketsByPair.Clear();
            _tokens.Clear();
            _level1Subscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _seenTrades.Clear();
            _tradeDeliveryOrder.Clear();
            _candleFingerprints.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _sentOrderTrades.Clear();
            _blockTimes.Clear();
            _blockTimeOrder.Clear();
            _nextMarketPoll = default;
            _nextPrivatePoll = default;
        }
    }
}
