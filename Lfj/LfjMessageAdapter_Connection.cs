namespace StockSharp.Lfj;

public partial class LfjMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rpcClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _rpcClient = new(RpcEndpoint, WalletAddress, PrivateKey)
            {
                Parent = this,
            };
            await RpcClient.VerifyChainAsync(cancellationToken);
            WalletAddress = RpcClient.IsWalletConfigured
                ? RpcClient.WalletAddress
                : null;
            _ = await GetOrLoadTokenAsync(
                LfjExtensions.NativeTokenAddress, cancellationToken);
            LfjMarketDefinition[] definitions;
            try
            {
                definitions = ParseMarketDefinitions();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "Configured LFJ pool definitions are invalid.",
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
                        "LFJ pool {0} loading failed: {1}",
                        definition.PoolId, error.Message);
                }
            }
            LfjMarket[] markets;
            using (_sync.EnterScope())
                markets = [.. _markets.Values];
            if (markets.Length == 0)
                throw errors.Count == 1
                    ? errors[0]
                    : new AggregateException(
                        "No LFJ pools could be loaded.", errors);
            if (!WebSocketEndpoint.IsEmpty())
            {
                try
                {
                    _socketClient = new(WebSocketEndpoint)
                    {
                        Parent = this,
                    };
                    _socketClient.LogReceived += OnSocketLog;
                    await _socketClient.ConnectAsync(markets,
                        cancellationToken);
                }
                catch (Exception error) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    this.AddWarningLog(
                        "LFJ WebSocket is unavailable; JSON-RPC log " +
                        "polling remains active: {0}", error.Message);
                    DisposeSocket();
                }
            }
            connectMsg.SessionId = RpcClient.IsWalletConfigured
                ? $"LFJ Avalanche {RpcClient.WalletAddress}"
                : "LFJ Avalanche public";
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
            if (_rpcClient is not null && hasMarketSubscriptions &&
                (_realtimeLogs.Count > 0 || now >= _nextMarketPoll))
            {
                _nextMarketPoll = now + PollingInterval;
                pollMarket = true;
            }
            if (_rpcClient is not null &&
                (_portfolioSubscriptions.Count > 0 ||
                    _orderSubscriptions.Count > 0 ||
                    _trackedSwaps.Values.Any(static swap =>
                        swap.State == OrderStates.Active)) &&
                now >= _nextPrivatePoll)
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
        LfjMarketDefinition definition,
        CancellationToken cancellationToken)
    {
        var pool = await RpcClient.GetPoolAsync(definition.PoolId,
            cancellationToken);
        LfjToken baseToken;
        LfjToken quoteToken;
        if (!definition.BaseToken.IsEmpty())
        {
            var baseAddress = definition.BaseToken.NormalizeAddress();
            var quoteAddress = definition.QuoteToken.NormalizeAddress();
            if (baseAddress.EqualsIgnoreCase(pool.TokenX.Address) &&
                quoteAddress.EqualsIgnoreCase(pool.TokenY.Address))
            {
                baseToken = pool.TokenX;
                quoteToken = pool.TokenY;
            }
            else if (baseAddress.EqualsIgnoreCase(pool.TokenY.Address) &&
                quoteAddress.EqualsIgnoreCase(pool.TokenX.Address))
            {
                baseToken = pool.TokenY;
                quoteToken = pool.TokenX;
            }
            else
            {
                throw new InvalidDataException(
                    $"Configured base/quote addresses do not match pool " +
                    $"'{pool.PoolId}'.");
            }
        }
        else
        {
            (baseToken, quoteToken) = OrientMarket(pool.TokenX, pool.TokenY);
        }
        var code = definition.SecurityCode.IsEmpty()
            ? CreateSecurityCode(baseToken, quoteToken, pool)
            : NormalizeSecurityCode(definition.SecurityCode);
        using (_sync.EnterScope())
        {
            if (_markets.ContainsKey(code))
                code += "-" + pool.PoolId[2..8].ToUpperInvariant();
        }
        var market = new LfjMarket
        {
            PoolId = pool.PoolId,
            PoolVersion = pool.PoolVersion,
            FactoryAddress = pool.FactoryAddress,
            RouterAddress = pool.RouterAddress,
            BinStep = pool.BinStep,
            TokenX = pool.TokenX,
            TokenY = pool.TokenY,
            BaseToken = baseToken,
            QuoteToken = quoteToken,
            SecurityCode = code,
        };
        var probe = ProbeVolume.ToBaseUnits(baseToken.Decimals);
        if (probe <= 0)
            throw new InvalidOperationException(
                "The configured LFJ quote probe volume rounds to zero.");
        _ = await RpcClient.GetQuoteAsync(market, LfjTradeTypes.ExactInput,
            probe, cancellationToken);
        _ = await RpcClient.GetQuoteAsync(market, LfjTradeTypes.ExactOutput,
            probe, cancellationToken);
        RegisterMarket(market);
    }

    private async ValueTask<LfjToken> GetOrLoadTokenAsync(
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

    private void RegisterMarket(LfjMarket market)
    {
        if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
            market.QuoteToken is null || market.TokenX is null ||
            market.TokenY is null || market.SecurityCode.IsEmpty() ||
            market.BinStep <= 0 || !System.Enum.IsDefined(market.PoolVersion))
            throw new InvalidDataException(
                "LFJ pool metadata is incomplete.");
        using (_sync.EnterScope())
        {
            if (_marketsByPool.ContainsKey(market.PoolId))
                throw new InvalidOperationException(
                    $"LFJ pool '{market.PoolId}' is configured twice.");
            if (_markets.ContainsKey(market.SecurityCode))
                throw new InvalidOperationException(
                    $"LFJ security code '{market.SecurityCode}' is " +
                    "configured twice.");
            _tokens[market.TokenX.Address] = market.TokenX;
            _tokens[market.TokenY.Address] = market.TokenY;
            _markets.Add(market.SecurityCode, market);
            _marketsByPool.Add(market.PoolId, market);
        }
    }

    private LfjMarketDefinition[] ParseMarketDefinitions()
    {
        if (Pools.IsEmpty())
            throw new InvalidOperationException(
                "At least one LFJ pool address must be configured.");
        var result = new List<LfjMarketDefinition>();
        foreach (var item in Pools.Split(';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            var fields = item.Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length is not 1 and not 3 and not 4)
                throw new FormatException(
                    "Each LFJ pool must use pool or " +
                    "pool|base-token|quote-token|security-code format.");
            if (fields.Length >= 3 &&
                (fields[1].IsEmpty() || fields[2].IsEmpty()))
                throw new FormatException(
                    "Both LFJ base and quote token addresses are " +
                    "required when orientation is configured.");
            result.Add(new()
            {
                PoolId = fields[0].NormalizeAddress(),
                BaseToken = fields.Length >= 3
                    ? fields[1].NormalizeAddress()
                    : null,
                QuoteToken = fields.Length >= 3
                    ? fields[2].NormalizeAddress()
                    : null,
                SecurityCode = fields.Length == 4
                    ? NormalizeSecurityCode(fields[3])
                    : null,
            });
        }
        return [.. result];
    }

    private static (LfjToken BaseToken, LfjToken QuoteToken)
        OrientMarket(LfjToken tokenX, LfjToken tokenY)
        => QuotePriority(tokenX.Symbol) > QuotePriority(tokenY.Symbol)
            ? (tokenY, tokenX)
            : (tokenX, tokenY);

    private static int QuotePriority(string symbol)
        => symbol?.ToUpperInvariant() switch
        {
            "USDC" => 100,
            "USDT" => 95,
            "DAI" => 90,
            "WAVAX" => 75,
            "AVAX" => 75,
            "BTC.B" => 65,
            "WBTC" => 65,
            _ => 0,
        };

    private static string CreateSecurityCode(LfjToken baseToken,
        LfjToken quoteToken, LfjPool pool)
    {
        if (!System.Enum.IsDefined(pool.PoolVersion) ||
            pool.PoolVersion != LfjPoolVersions.V22 || pool.BinStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(pool));
        return NormalizeSecurityCode(
            $"{baseToken.Symbol}-{quoteToken.Symbol}-LB{pool.BinStep}");
    }

    private static string NormalizeSecurityCode(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
        if (value.Length > 64 || value.Any(static ch =>
            !char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
            throw new FormatException(
                $"Invalid LFJ security code '{value}'.");
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

    private void DisposeSocket()
    {
        if (_socketClient is null)
            return;
        _socketClient.LogReceived -= OnSocketLog;
        _socketClient.Dispose();
        _socketClient = null;
    }

    private void DisposeClients()
    {
        DisposeSocket();
        _rpcClient?.Dispose();
        _rpcClient = null;
        ClearState();
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _marketsByPool.Clear();
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
            _realtimeLogs.Clear();
            _blockTimes.Clear();
            _blockTimeOrder.Clear();
            _nextMarketPoll = default;
            _nextPrivatePoll = default;
        }
    }
}
