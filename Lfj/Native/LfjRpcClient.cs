namespace StockSharp.Lfj.Native;

sealed class LfjRpcClient : BaseLogReceiver
{
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly byte[] _privateKey;
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private long _requestId;
    private DateTime _nextRequest;

    public LfjRpcClient(string endpoint, string walletAddress,
        SecureString privateKey)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
            !(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
                _endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
            throw new ArgumentException(
                "EVM JSON-RPC endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        var privateKeyText = privateKey.IsEmpty()
            ? null
            : privateKey.UnSecure().Trim();
        if (!privateKeyText.IsEmpty())
        {
            try
            {
                var key = new EthECKey(privateKeyText);
                var keyHex = privateKeyText.StartsWith("0x",
                    StringComparison.OrdinalIgnoreCase)
                    ? privateKeyText[2..]
                    : privateKeyText;
                _privateKey = keyHex.HexToByteArray();
                var derived = key.GetPublicAddress().NormalizeAddress();
                if (!walletAddress.IsEmpty() &&
                    !derived.EqualsIgnoreCase(
                        walletAddress.NormalizeAddress()))
                    throw new ArgumentException(
                        "The configured wallet address does not match the " +
                        "private key.", nameof(walletAddress));
                WalletAddress = derived;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new ArgumentException(
                    "Invalid EVM private key.", nameof(privateKey), error);
            }
        }
        else
        {
            WalletAddress = walletAddress.IsEmpty()
                ? LfjExtensions.NativeTokenAddress
                : walletAddress.NormalizeAddress();
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-LFJ-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "LFJ_JSON-RPC";

    public string WalletAddress { get; }

    public bool IsSigningAvailable => _privateKey is not null;

    public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
        LfjExtensions.NativeTokenAddress);

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _requestGate.Dispose();
        _transactionGate.Dispose();
        if (_privateKey is not null)
            CryptographicOperations.ZeroMemory(_privateKey);
        base.DisposeManaged();
    }

    public async ValueTask VerifyChainAsync(
        CancellationToken cancellationToken)
    {
        var chainId = (await SendAsync<LfjRpcEmptyParameters, string>(
            "eth_chainId", new(), true, cancellationToken)).ParseInteger();
        if (chainId != new BigInteger(LfjExtensions.ChainId))
            throw new InvalidOperationException(
                $"JSON-RPC is connected to chain {chainId}, but " +
                $"Avalanche C-Chain {LfjExtensions.ChainId} is required.");
    }

    public async ValueTask<LfjToken> GetTokenAsync(string address,
        CancellationToken cancellationToken)
    {
        address = address.NormalizeAddress();
        if (address.IsNativeToken())
            return new()
            {
                Address = address,
                Symbol = "AVAX",
                Name = "Avalanche",
                Decimals = 18,
            };
        var decimalsValue = await CallContractAsync(address, "0x313ce567",
            cancellationToken);
        var decimalsInteger = decimalsValue.ParseInteger();
        if (decimalsInteger < 0 || decimalsInteger > 255)
            throw new InvalidDataException(
                $"Token '{address}' returned invalid decimals value.");
        var symbol = await TryReadTextAsync(address, "0x95d89b41",
            cancellationToken);
        var name = await TryReadTextAsync(address, "0x06fdde03",
            cancellationToken);
        symbol = symbol.NormalizeTokenSymbol(address);
        return new()
        {
            Address = address,
            Symbol = symbol,
            Name = name.NormalizeTokenName(symbol),
            Decimals = (int)decimalsInteger,
        };
    }

    public async ValueTask<BigInteger> GetBalanceAsync(LfjToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            return (await SendAsync<LfjRpcAddressTagParameters,
                string>("eth_getBalance", new()
                {
                    Address = WalletAddress,
                    BlockTag = "latest",
                }, true, cancellationToken)).ParseInteger();
        var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
        return (await CallContractAsync(token.Address, data,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<LfjPool> GetPoolAsync(string poolAddress,
        CancellationToken cancellationToken)
    {
        poolAddress = poolAddress.NormalizeAddress();
        var code = await SendAsync<LfjRpcAddressTagParameters, string>(
            "eth_getCode", new()
            {
                Address = poolAddress,
                BlockTag = "latest",
            }, true, cancellationToken);
        if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
            code.EqualsIgnoreCase("0x0"))
            throw new InvalidDataException(
                $"LFJ pool '{poolAddress}' is not a contract.");
        var factory = LfjExtensions.ReadAbiAddress(
            await CallContractAsync(poolAddress,
                LfjExtensions.EncodeStaticCall("getFactory()"),
                cancellationToken), 0);
        if (!factory.EqualsIgnoreCase(LfjExtensions.FactoryV22Address))
            throw new InvalidDataException(
                $"Pool '{poolAddress}' was not created by the official LFJ " +
                "V2.2 factory.");
        var tokenXAddress = LfjExtensions.ReadAbiAddress(
            await CallContractAsync(poolAddress,
                LfjExtensions.EncodeStaticCall("getTokenX()"),
                cancellationToken), 0);
        var tokenYAddress = LfjExtensions.ReadAbiAddress(
            await CallContractAsync(poolAddress,
                LfjExtensions.EncodeStaticCall("getTokenY()"),
                cancellationToken), 0);
        if (tokenXAddress == tokenYAddress || tokenXAddress.IsNativeToken() ||
            tokenYAddress.IsNativeToken())
            throw new InvalidDataException(
                $"LFJ pool '{poolAddress}' returned invalid token addresses.");
        var binStepValue = LfjExtensions.ReadAbiWord(
            await CallContractAsync(poolAddress,
                LfjExtensions.EncodeStaticCall("getBinStep()"),
                cancellationToken), 0);
        if (binStepValue <= 0 || binStepValue > ushort.MaxValue)
            throw new InvalidDataException(
                $"LFJ pool '{poolAddress}' returned invalid bin step " +
                $"'{binStepValue}'.");
        var binStep = (int)binStepValue;
        var factoryData = LfjExtensions.EncodeStaticCall(
            "getLBPairInformation(address,address,uint256)",
            LfjExtensions.AbiAddress(tokenXAddress),
            LfjExtensions.AbiAddress(tokenYAddress),
            LfjExtensions.AbiWord(binStep));
        var information = await CallContractAsync(factory, factoryData,
            cancellationToken);
        if (LfjExtensions.ReadAbiWord(information, 0) != binStep ||
            !LfjExtensions.ReadAbiAddress(information, 1).EqualsIgnoreCase(
                poolAddress) || LfjExtensions.ReadAbiWord(information, 3) != 0)
            throw new InvalidDataException(
                $"LFJ factory does not expose pool '{poolAddress}' as an " +
                "active V2.2 route.");
        return new()
        {
            PoolId = poolAddress,
            PoolVersion = LfjPoolVersions.V22,
            FactoryAddress = factory,
            RouterAddress = LfjExtensions.RouterV22Address.NormalizeAddress(),
            BinStep = binStep,
            TokenX = await GetTokenAsync(tokenXAddress, cancellationToken),
            TokenY = await GetTokenAsync(tokenYAddress, cancellationToken),
        };
    }

    public async ValueTask<LfjQuote> GetQuoteAsync(
        LfjMarket market, LfjTradeTypes tradeType,
        BigInteger amount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (!System.Enum.IsDefined(tradeType))
            throw new ArgumentOutOfRangeException(nameof(tradeType), tradeType,
                "Unsupported LFJ trade type.");
        if (!System.Enum.IsDefined(market.PoolVersion) ||
            market.PoolVersion != LfjPoolVersions.V22)
            throw new ArgumentOutOfRangeException(nameof(market.PoolVersion),
                market.PoolVersion, "Unsupported LFJ pool version.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount >= BigInteger.One << 128)
            throw new ArgumentOutOfRangeException(nameof(amount),
                "LFJ swap amount exceeds uint128.");
        var isExactInput = tradeType == LfjTradeTypes.ExactInput;
        var tokenIn = isExactInput
            ? market.BaseToken.Address
            : market.QuoteToken.Address;
        var tokenOut = isExactInput
            ? market.QuoteToken.Address
            : market.BaseToken.Address;
        var isXToY = tokenIn.EqualsIgnoreCase(market.TokenX.Address) &&
            tokenOut.EqualsIgnoreCase(market.TokenY.Address);
        var isYToX = tokenIn.EqualsIgnoreCase(market.TokenY.Address) &&
            tokenOut.EqualsIgnoreCase(market.TokenX.Address);
        if (!isXToY && !isYToX)
            throw new InvalidDataException(
                "LFJ quote tokens do not match the configured pool.");
        var signature = isExactInput
            ? "getSwapOut(uint128,bool)"
            : "getSwapIn(uint128,bool)";
        var response = await CallContractAsync(market.PoolId,
            LfjExtensions.EncodeStaticCall(signature,
                LfjExtensions.AbiWord(amount),
                LfjExtensions.AbiWord(isXToY
                    ? BigInteger.One
                    : BigInteger.Zero)), cancellationToken);
        var first = LfjExtensions.ReadAbiWord(response, 0);
        var second = LfjExtensions.ReadAbiWord(response, 1);
        var fee = LfjExtensions.ReadAbiWord(response, 2);
        var input = isExactInput ? amount : first;
        var output = isExactInput ? second : amount;
        var unfilled = isExactInput ? first : second;
        if (unfilled != 0)
            throw new InvalidOperationException(
                "LFJ pool has insufficient liquidity for the requested quote.");
        if (input <= 0 || output <= 0 || fee < 0 || fee > input)
            throw new InvalidDataException(
                "LFJ pool returned invalid quote amounts.");
        return new()
        {
            InputAmount = input,
            OutputAmount = output,
            FeeAmount = fee,
        };
    }

    public async ValueTask<BigInteger> GetAllowanceAsync(
        LfjToken token, string spender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            return BigInteger.Pow(2, 256) - 1;
        var data = LfjExtensions.EncodeStaticCall(
            "allowance(address,address)",
            LfjExtensions.AbiAddress(WalletAddress),
            LfjExtensions.AbiAddress(spender));
        return (await CallContractAsync(token.Address, data,
            cancellationToken)).ParseInteger();
    }

    public LfjTransaction CreateApprovalTransaction(
        LfjToken token, string spender, BigInteger amount)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            throw new NotSupportedException(
                "A native token does not use ERC-20 approvals.");
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return new()
        {
            To = token.Address.NormalizeAddress(),
            Data = LfjExtensions.EncodeStaticCall(
                "approve(address,uint256)",
                LfjExtensions.AbiAddress(spender),
                LfjExtensions.AbiWord(amount)),
            Value = BigInteger.Zero,
        };
    }

    public LfjTransaction CreateSwapTransaction(
        LfjMarket market, LfjTradeTypes tradeType,
        BigInteger amount, LfjQuote quote, decimal slippageTolerance,
        DateTime deadline)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(quote);
        if (!System.Enum.IsDefined(tradeType))
            throw new ArgumentOutOfRangeException(nameof(tradeType), tradeType,
                "Unsupported LFJ trade type.");
        if (!System.Enum.IsDefined(market.PoolVersion) ||
            market.PoolVersion != LfjPoolVersions.V22)
            throw new ArgumentOutOfRangeException(nameof(market.PoolVersion),
                market.PoolVersion, "Unsupported LFJ pool version.");
        if (amount <= 0 || quote.InputAmount <= 0 ||
            quote.OutputAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var isExactInput = tradeType == LfjTradeTypes.ExactInput;
        if (isExactInput && quote.InputAmount != amount ||
            !isExactInput && quote.OutputAmount != amount)
            throw new InvalidDataException(
                "LFJ quote does not match the requested swap amount.");
        var tokenIn = isExactInput
            ? market.BaseToken.Address
            : market.QuoteToken.Address;
        var tokenOut = isExactInput
            ? market.QuoteToken.Address
            : market.BaseToken.Address;
        if (tokenIn.IsNativeToken() || tokenOut.IsNativeToken())
            throw new NotSupportedException(
                "Use wrapped native tokens for direct LFJ pools.");
        var slippageBps = new BigInteger(slippageTolerance * 100m);
        var minimumOutput = quote.OutputAmount *
            (10_000 - slippageBps) / 10_000;
        var maximumInput = (quote.InputAmount *
            (10_000 + slippageBps) + 9_999) / 10_000;
        if (minimumOutput <= 0 || maximumInput <= 0)
            throw new InvalidOperationException(
                "Slippage-adjusted LFJ amount is non-positive.");
        var deadlineValue = new BigInteger(deadline.ToUnixSeconds());
        var data = EncodeSwap(isExactInput, quote.InputAmount,
            quote.OutputAmount, minimumOutput, maximumInput, market.BinStep,
            market.PoolVersion, tokenIn, tokenOut, deadlineValue);
        return new()
        {
            To = market.RouterAddress.NormalizeAddress(),
            Data = data,
            Value = BigInteger.Zero,
        };
    }

    public string GetSpender(LfjMarket market)
    {
        ArgumentNullException.ThrowIfNull(market);
        return market.RouterAddress.NormalizeAddress();
    }

    public async ValueTask<BigInteger> EstimateGasAsync(
        LfjTransaction transaction,
        CancellationToken cancellationToken)
    {
        var call = ToRpcCall(transaction);
        return (await SendAsync<LfjRpcCallOnlyParameters, string>(
            "eth_estimateGas", new() { Call = call }, true,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<string> SendTransactionAsync(
        LfjTransaction transaction,
        CancellationToken cancellationToken)
    {
        EnsureSigningAvailable();
        ValidateTransaction(transaction);
        await _transactionGate.WaitAsync(cancellationToken);
        try
        {
            var nonce = (await SendAsync<LfjRpcAddressTagParameters,
                string>("eth_getTransactionCount", new()
                {
                    Address = WalletAddress,
                    BlockTag = "pending",
                }, true, cancellationToken)).ParseInteger();
            var estimated = await EstimateGasAsync(transaction,
                cancellationToken);
            if (estimated <= 0)
                throw new InvalidDataException(
                    "The transaction gas estimate must be positive.");
            var gasLimit = (estimated * 120 + 99) / 100;
            var data = transaction.Data[2..].HexToByteArray();
            byte[] encoded;
            var fees = await TryGetEip1559FeesAsync(cancellationToken);
            if (fees is { } eip1559)
            {
                var tx = new Transaction1559(
                    new BigInteger(LfjExtensions.ChainId), nonce,
                    eip1559.PriorityFee,
                    eip1559.MaximumFee, gasLimit,
                    transaction.To.NormalizeAddress(), transaction.Value,
                    transaction.Data, null);
                new Transaction1559Signer().SignTransaction(_privateKey, tx);
                encoded = tx.GetRLPEncoded();
            }
            else
            {
                var gasPrice = (await SendAsync<
                    LfjRpcEmptyParameters, string>("eth_gasPrice",
                    new(), true, cancellationToken)).ParseInteger();
                var tx = new LegacyTransactionChainId(
                    nonce.ToBytesForRLPEncoding(),
                    gasPrice.ToBytesForRLPEncoding(),
                    gasLimit.ToBytesForRLPEncoding(),
                    transaction.To.NormalizeAddress().HexToByteArray(),
                    transaction.Value.ToBytesForRLPEncoding(), data,
                    new BigInteger(LfjExtensions.ChainId)
                        .ToBytesForRLPEncoding());
                new LegacyTransactionSigner().SignTransaction(_privateKey,
                    tx);
                encoded = tx.GetRLPEncoded();
            }
            var hash = await SendAsync<LfjRpcValueParameters, string>(
                "eth_sendRawTransaction",
                new() { Value = encoded.ToHex(true) }, false,
                cancellationToken);
            return NormalizeHash(hash);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public async ValueTask<LfjRpcReceipt> GetReceiptAsync(string hash,
        CancellationToken cancellationToken)
    {
        hash = NormalizeHash(hash);
        var receipt = await SendAsync<LfjRpcValueParameters, LfjRpcReceipt>(
            "eth_getTransactionReceipt", new()
            {
                Value = hash,
            }, true, cancellationToken);
        if (receipt is null)
            return null;
        if (!receipt.TransactionHash.IsEmpty() &&
            !NormalizeHash(receipt.TransactionHash).EqualsIgnoreCase(hash))
            throw new InvalidDataException(
                "JSON-RPC returned a receipt for a different transaction.");
        if (receipt.BlockNumber.IsEmpty())
            throw new InvalidDataException(
                $"Transaction '{hash}' receipt has no block number.");
        _ = receipt.BlockNumber.ParseInteger();
        return receipt;
    }

    public async ValueTask<BigInteger> GetLatestBlockNumberAsync(
        CancellationToken cancellationToken)
        => (await SendAsync<LfjRpcEmptyParameters, string>(
            "eth_blockNumber", new(), true, cancellationToken)).ParseInteger();

    public async ValueTask<LfjRpcBlock> GetBlockAsync(
        BigInteger blockNumber, CancellationToken cancellationToken)
    {
        if (blockNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(blockNumber));
        var block = await SendAsync<LfjRpcTagBooleanParameters,
            LfjRpcBlock>("eth_getBlockByNumber", new()
            {
                BlockTag = blockNumber.ToRpcHex(),
                IsTransactionsIncluded = false,
            }, true, cancellationToken);
        return block ?? throw new InvalidDataException(
            $"Avalanche block '{blockNumber}' was not found.");
    }

    public async ValueTask<DateTime> GetBlockTimeAsync(
        BigInteger blockNumber, CancellationToken cancellationToken)
    {
        var block = await GetBlockAsync(blockNumber, cancellationToken);
        if (block.Timestamp.IsEmpty())
            throw new InvalidDataException(
                $"Avalanche block '{blockNumber}' has no timestamp.");
        return block.Timestamp.ParseInteger().ToUtcTime();
    }

    public async ValueTask<BigInteger> FindBlockAsync(DateTime time,
        BigInteger latestBlock, CancellationToken cancellationToken)
    {
        if (latestBlock < 0)
            throw new ArgumentOutOfRangeException(nameof(latestBlock));
        time = time.ToUniversalTime();
        if (time >= await GetBlockTimeAsync(latestBlock, cancellationToken))
            return latestBlock;
        var low = BigInteger.Zero;
        var high = latestBlock;
        while (low < high)
        {
            var middle = (low + high) / 2;
            var blockTime = await GetBlockTimeAsync(middle,
                cancellationToken);
            if (blockTime < time)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    public ValueTask<LfjRpcLog[]> GetLogsAsync(LfjMarket market,
        BigInteger fromBlock, BigInteger toBlock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (fromBlock < 0 || toBlock < fromBlock)
            throw new ArgumentOutOfRangeException(nameof(fromBlock));
        return SendAsync<LfjRpcLogsParameters, LfjRpcLog[]>(
            "eth_getLogs", new()
            {
                Filter = new()
                {
                    FromBlock = fromBlock.ToRpcHex(),
                    ToBlock = toBlock.ToRpcHex(),
                    Address = market.PoolId,
                    Topics = [market.PoolVersion.GetSwapTopic()],
                },
            }, true, cancellationToken);
    }

    public async ValueTask<LfjRpcReceipt> WaitForReceiptAsync(
        string hash, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var receipt = await GetReceiptAsync(hash, cancellationToken);
            if (receipt is not null)
                return receipt;
            var remaining = deadline - DateTime.UtcNow;
            await Task.Delay(remaining.Min(TimeSpan.FromSeconds(2)),
                cancellationToken);
        }
        throw new TimeoutException(
            $"Transaction '{hash}' was not mined within {timeout}.");
    }

    private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
        TryGetEip1559FeesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var block = await SendAsync<
                LfjRpcTagBooleanParameters, LfjRpcBlock>(
                "eth_getBlockByNumber", new()
                {
                    BlockTag = "latest",
                    IsTransactionsIncluded = false,
                }, true, cancellationToken);
            if (block?.BaseFeePerGas.IsEmpty() != false)
                return null;
            var baseFee = block.BaseFeePerGas.ParseInteger();
            var priority = (await SendAsync<
                LfjRpcEmptyParameters, string>(
                "eth_maxPriorityFeePerGas", new(), true,
                cancellationToken)).ParseInteger();
            if (baseFee <= 0 || priority < 0)
                return null;
            return (priority, baseFee * 2 + priority);
        }
        catch (Exception error) when (
            !cancellationToken.IsCancellationRequested)
        {
            this.AddDebugLog(
                "EIP-1559 fee discovery failed; using legacy gas price: {0}",
                error.Message);
            return null;
        }
    }

    private async ValueTask<string> CallContractAsync(string address,
        string data, CancellationToken cancellationToken)
        => await SendAsync<LfjRpcCallParameters, string>("eth_call",
            new()
            {
                Call = new()
                {
                    From = WalletAddress,
                    To = address.NormalizeAddress(),
                    Data = data,
                },
                BlockTag = "latest",
            }, true, cancellationToken);

    private async ValueTask<string> TryReadTextAsync(string address,
        string selector, CancellationToken cancellationToken)
    {
        try
        {
            return DecodeAbiText(await CallContractAsync(address, selector,
                cancellationToken));
        }
        catch (Exception error) when (
            !cancellationToken.IsCancellationRequested)
        {
            this.AddWarningLog(
                "Unable to read token text metadata from {0}: {1}",
                address, error.Message);
            return null;
        }
    }

    private async ValueTask<TResult> SendAsync<TParameters, TResult>(
        string method, TParameters parameters, bool isRead,
        CancellationToken cancellationToken)
        where TParameters : LfjRpcParameters
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var payload = JsonConvert.SerializeObject(
            new LfjRpcRequest<TParameters>
            {
                Id = requestId,
                Method = method,
                Parameters = parameters,
            }, _jsonSettings);
        for (var attempt = 0; ; attempt++)
        {
            await WaitForRequestAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8,
                    "application/json"),
            };
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (isRead && attempt < 3 && (response.StatusCode ==
                    (HttpStatusCode)429 || (int)response.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(
                    250 * (1 << attempt)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"JSON-RPC HTTP {(int)response.StatusCode}: " +
                    Truncate(body));
            LfjRpcResponse<TResult> rpc;
            try
            {
                rpc = JsonConvert.DeserializeObject<
                    LfjRpcResponse<TResult>>(body, _jsonSettings);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "JSON-RPC returned an unexpected response shape.",
                    error);
            }
            if (rpc is null || rpc.Id != requestId)
                throw new InvalidDataException(
                    "JSON-RPC returned an invalid response identifier.");
            if (rpc.Error is not null)
                throw new InvalidOperationException(
                    $"JSON-RPC {rpc.Error.Code}: " +
                    (rpc.Error.Message ?? "request rejected"));
            return rpc.Result;
        }
    }

    private async ValueTask WaitForRequestAsync(
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequest - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private string EncodeSwap(bool isExactInput, BigInteger inputAmount,
        BigInteger outputAmount, BigInteger minimumOutput,
        BigInteger maximumInput, int binStep, LfjPoolVersions version,
        string tokenIn, string tokenOut, BigInteger deadline)
    {
        if (binStep <= 0 || binStep > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(binStep));
        if (!System.Enum.IsDefined(version) || version != LfjPoolVersions.V22)
            throw new ArgumentOutOfRangeException(nameof(version), version,
                "Unsupported LFJ pool version.");
        var signature = isExactInput
            ? "swapExactTokensForTokens(uint256,uint256," +
                "(uint256[],uint8[],address[]),address,uint256)"
            : "swapTokensForExactTokens(uint256,uint256," +
                "(uint256[],uint8[],address[]),address,uint256)";
        var firstAmount = isExactInput ? inputAmount : outputAmount;
        var secondAmount = isExactInput ? minimumOutput : maximumInput;
        return "0x" + LfjExtensions.AbiSelector(signature) +
            LfjExtensions.AbiWord(firstAmount) +
            LfjExtensions.AbiWord(secondAmount) +
            LfjExtensions.AbiWord(160) +
            LfjExtensions.AbiAddress(WalletAddress) +
            LfjExtensions.AbiWord(deadline) +
            LfjExtensions.AbiWord(96) +
            LfjExtensions.AbiWord(160) +
            LfjExtensions.AbiWord(224) +
            LfjExtensions.AbiWord(1) +
            LfjExtensions.AbiWord(binStep) +
            LfjExtensions.AbiWord(1) +
            LfjExtensions.AbiWord((int)version) +
            LfjExtensions.AbiWord(2) +
            LfjExtensions.AbiAddress(tokenIn) +
            LfjExtensions.AbiAddress(tokenOut);
    }

    private void ValidateTransaction(LfjTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _ = transaction.To.NormalizeAddress();
        if (transaction.Data.IsEmpty() ||
            !transaction.Data.StartsWith("0x",
                StringComparison.OrdinalIgnoreCase) ||
            transaction.Data.Length <= 2 || transaction.Data[2..].Any(
                static ch => !Uri.IsHexDigit(ch)) ||
            (transaction.Data.Length - 2) % 2 != 0)
            throw new InvalidDataException(
                "Transaction calldata must be a non-empty even-length hex " +
                "string.");
        if (transaction.Value < 0)
            throw new InvalidDataException(
                "Transaction value cannot be negative.");
    }

    private LfjRpcCall ToRpcCall(LfjTransaction transaction)
        => new()
        {
            From = WalletAddress,
            To = transaction.To.NormalizeAddress(),
            Data = transaction.Data,
            Value = transaction.Value.ToRpcHex(),
        };

    private void EnsureSigningAvailable()
    {
        if (!IsSigningAvailable)
            throw new InvalidOperationException(
                "An EVM private key is required for LFJ transactions.");
    }

    private static string DecodeAbiText(string value)
    {
        if (value.IsEmpty() || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase))
            return null;
        var bytes = value[2..].HexToByteArray();
        if (bytes.Length == 32)
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
        if (bytes.Length < 64)
            return null;
        var offset = ReadAbiInteger(bytes, 0);
        if (offset < 0 || offset > int.MaxValue)
            return null;
        var offsetValue = (int)offset;
        if (offsetValue > bytes.Length - 32)
            return null;
        var length = ReadAbiInteger(bytes, offsetValue);
        if (length < 0 || length > int.MaxValue)
            return null;
        var lengthValue = (int)length;
        if (lengthValue > bytes.Length - offsetValue - 32)
            return null;
        return Encoding.UTF8.GetString(bytes, offsetValue + 32,
            lengthValue).Trim();
    }

    private static BigInteger ReadAbiInteger(byte[] bytes, int offset)
    {
        var littleEndian = new byte[33];
        for (var index = 0; index < 32; index++)
            littleEndian[index] = bytes[offset + 31 - index];
        return new BigInteger(littleEndian);
    }

    private static string NormalizeHash(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.Length != 66 || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
            static ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException(
                $"Invalid EVM transaction hash '{value}'.");
        return "0x" + value[2..].ToLowerInvariant();
    }

    private static string Truncate(string value)
    {
        value = value?.Trim();
        return value.IsEmpty()
            ? "request rejected"
            : value.Truncate(512, string.Empty);
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "JSON-RPC response exceeds the 8 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(block, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "JSON-RPC response exceeds the 8 MiB safety limit.");
            target.Write(block, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
