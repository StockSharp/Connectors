namespace StockSharp.Uniswap;

public partial class UniswapMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_tradingClient is not null || _rpcClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (Chain == UniswapChains.ZkSync &&
            RouterVersion != UniswapRouterVersions.Version2_0)
            throw new InvalidOperationException(
                "zkSync supports Universal Router 2.0 only.");
        if (Chain == UniswapChains.RobinhoodChain &&
            RouterVersion == UniswapRouterVersions.Version2_0)
            throw new InvalidOperationException(
                "Robinhood Chain has no Universal Router 2.0 deployment.");
        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _rpcClient = new(RpcEndpoint, Chain, WalletAddress, PrivateKey)
            {
                Parent = this,
            };
            await RpcClient.VerifyChainAsync(cancellationToken);
            WalletAddress = RpcClient.WalletAddress;
            if (Chain.HasNativeToken())
                _ = await GetOrLoadTokenAsync(
                    UniswapExtensions.NativeTokenAddress, cancellationToken);
            _tradingClient = new(TradingEndpoint, Token, RouterVersion)
            {
                Parent = this,
            };
            if (!GraphApiKey.IsEmpty())
            {
                if (Chain != UniswapChains.Ethereum &&
                    SubgraphId.EqualsIgnoreCase(_defaultV3SubgraphId))
                {
                    this.AddWarningLog(
                        "The default Uniswap v3 subgraph is Ethereum-only; " +
                        "configure a chain-specific SubgraphId to enable " +
                        "discovery, ticks, and candles on {0}.", Chain);
                }
                else
                {
                    _graphClient = new(GraphApiKey, SubgraphId)
                    {
                        Parent = this,
                    };
                }
            }

            var errors = new List<Exception>();
            UniswapMarketDefinition[] definitions;
            try
            {
                definitions = ParseMarketDefinitions();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "Configured Uniswap market definitions are invalid.",
                    error);
            }
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
                        "Uniswap pool {0} loading failed: {1}",
                        definition.PoolId, error.Message);
                }
            }
            if (_graphClient is not null)
            {
                try
                {
                    await RegisterDiscoveredPoolsAsync(
                        await GraphClient.GetPoolsAsync(
                            MaximumDiscoveredPools, cancellationToken));
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    this.AddWarningLog(
                        "Uniswap v3 subgraph discovery failed: {0}",
                        error.Message);
                }
            }
            using (_sync.EnterScope())
                if (_markets.Count == 0)
                    throw errors.Count == 1
                        ? errors[0]
                        : new AggregateException(
                            "No Uniswap markets could be loaded.", errors);

            connectMsg.SessionId = $"Uniswap {Chain} " +
                RpcClient.WalletAddress;
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
        var pollMarket = false;
        var pollPrivate = false;
        using (_sync.EnterScope())
        {
            if (_tradingClient is not null &&
                (_level1Subscriptions.Count > 0 ||
                    _tickSubscriptions.Count > 0 ||
                    _candleSubscriptions.Count > 0) &&
                CurrentTime >= _nextMarketPoll)
            {
                _nextMarketPoll = CurrentTime + PollingInterval;
                pollMarket = true;
            }
            if (_rpcClient is not null &&
                (_portfolioSubscriptions.Count > 0 ||
                    _orderSubscriptions.Count > 0 ||
                    _trackedSwaps.Values.Any(static swap =>
                        swap.State == OrderStates.Active)) &&
                CurrentTime >= _nextPrivatePoll)
            {
                _nextPrivatePoll = CurrentTime + PollingInterval;
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
        UniswapMarketDefinition definition,
        CancellationToken cancellationToken)
    {
        var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
            cancellationToken);
        var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
            cancellationToken);
        var ordered = OrderTokens(baseToken, quoteToken);
        RegisterMarket(new()
        {
            PoolId = definition.PoolId.NormalizeAddress(),
            Token0 = ordered.Token0,
            Token1 = ordered.Token1,
            BaseToken = baseToken,
            QuoteToken = quoteToken,
        });
    }

    private ValueTask RegisterDiscoveredPoolsAsync(
        IEnumerable<UniswapPool> pools)
    {
        foreach (var pool in pools ?? [])
        {
            if (pool?.Id.IsEmpty() != false || pool.Token0 is null ||
                pool.Token1 is null)
                continue;
            if (!int.TryParse(pool.Token0.Decimals,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var token0Decimals) ||
                !int.TryParse(pool.Token1.Decimals,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var token1Decimals) ||
                token0Decimals is < 0 or > 255 ||
                token1Decimals is < 0 or > 255)
                continue;
            UniswapToken token0;
            UniswapToken token1;
            string poolId;
            try
            {
                poolId = pool.Id.NormalizeAddress();
                token0 = new()
                {
                    Address = pool.Token0.Id.NormalizeAddress(),
                    Symbol = pool.Token0.Symbol?.Trim().ToUpperInvariant(),
                    Name = pool.Token0.Name?.Trim(),
                    Decimals = token0Decimals,
                };
                token1 = new()
                {
                    Address = pool.Token1.Id.NormalizeAddress(),
                    Symbol = pool.Token1.Symbol?.Trim().ToUpperInvariant(),
                    Name = pool.Token1.Name?.Trim(),
                    Decimals = token1Decimals,
                };
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (token0.Symbol.IsEmpty() || token1.Symbol.IsEmpty())
                continue;
            var oriented = OrientMarket(token0, token1);
            RegisterMarket(new()
            {
                PoolId = poolId,
                Token0 = token0,
                Token1 = token1,
                BaseToken = oriented.BaseToken,
                QuoteToken = oriented.QuoteToken,
                TotalValueLockedUsd =
                    pool.TotalValueLockedUsd.ToDecimalInvariant() ?? 0m,
            });
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask<UniswapToken> GetOrLoadTokenAsync(string address,
        CancellationToken cancellationToken)
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

    private void RegisterMarket(UniswapMarket market)
    {
        if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
            market.QuoteToken is null || market.Token0 is null ||
            market.Token1 is null || market.BaseToken.Symbol.IsEmpty() ||
            market.QuoteToken.Symbol.IsEmpty())
            return;
        using (_sync.EnterScope())
        {
            _tokens[market.Token0.Address] = market.Token0;
            _tokens[market.Token1.Address] = market.Token1;
            if (_markets.TryGetValue(market.SecurityCode, out var existing))
            {
                if (existing.BaseToken.Address.EqualsIgnoreCase(
                        market.BaseToken.Address) &&
                    existing.QuoteToken.Address.EqualsIgnoreCase(
                        market.QuoteToken.Address) &&
                    market.TotalValueLockedUsd >
                        existing.TotalValueLockedUsd)
                    _markets[market.SecurityCode] = market;
                return;
            }
            _markets.Add(market.SecurityCode, market);
        }
    }

    private UniswapMarketDefinition[] ParseMarketDefinitions()
    {
        if (Markets.IsEmpty() || (Chain != UniswapChains.Ethereum &&
            Markets.EqualsIgnoreCase(_defaultMarkets)))
            return [];
        var result = new List<UniswapMarketDefinition>();
        foreach (var item in Markets.Split(';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            var fields = item.Split('|',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
            if (fields.Length != 3)
                throw new FormatException(
                    "Each Uniswap market must use " +
                    "pool|base-token|quote-token format.");
            result.Add(new()
            {
                PoolId = fields[0].NormalizeAddress(),
                BaseToken = fields[1].NormalizeAddress(),
                QuoteToken = fields[2].NormalizeAddress(),
            });
        }
        return [.. result];
    }

    private static (UniswapToken Token0, UniswapToken Token1) OrderTokens(
        UniswapToken left, UniswapToken right)
        => AddressValue(left.Address) < AddressValue(right.Address)
            ? (left, right)
            : (right, left);

    private static (UniswapToken BaseToken, UniswapToken QuoteToken)
        OrientMarket(UniswapToken token0, UniswapToken token1)
        => QuotePriority(token0.Symbol) > QuotePriority(token1.Symbol)
            ? (token1, token0)
            : (token0, token1);

    private static int QuotePriority(string symbol)
        => symbol?.ToUpperInvariant() switch
        {
            "USDC" => 100,
            "USDT" => 95,
            "DAI" => 90,
            "WETH" => 70,
            "ETH" => 70,
            "WBTC" => 60,
            _ => 0,
        };

    private static BigInteger AddressValue(string address)
        => BigInteger.Parse("0" + address.NormalizeAddress()[2..],
            NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

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
        _graphClient?.Dispose();
        _graphClient = null;
        _tradingClient?.Dispose();
        _tradingClient = null;
        _rpcClient?.Dispose();
        _rpcClient = null;
        ClearState();
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _tokens.Clear();
            _level1Subscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _seenTrades.Clear();
            _tradeDeliveryOrder.Clear();
            _candleFingerprints.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedSwaps.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _nextMarketPoll = default;
            _nextPrivatePoll = default;
        }
    }
}
