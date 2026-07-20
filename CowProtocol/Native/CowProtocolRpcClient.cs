namespace StockSharp.CowProtocol.Native;

sealed class CowProtocolRpcClient : BaseLogReceiver
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
    private readonly CowProtocolSigner _signer;
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

    public CowProtocolRpcClient(string endpoint, CowProtocolChains chain,
        string walletAddress, SecureString privateKey)
    {
        if (!System.Enum.IsDefined(chain))
            throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain.");
        Chain = chain;
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
                _signer = new(privateKeyText, chain);
                var keyHex = privateKeyText.StartsWith("0x",
                    StringComparison.OrdinalIgnoreCase)
                    ? privateKeyText[2..]
                    : privateKeyText;
                _privateKey = keyHex.HexToByteArray();
                if (_privateKey.Length != 32)
                    throw new ArgumentException(
                        "An EVM private key must contain exactly 32 bytes.");
                if (!walletAddress.IsEmpty() &&
                    !_signer.Address.EqualsIgnoreCase(
                        walletAddress.NormalizeAddress()))
                    throw new ArgumentException(
                        "The configured wallet address does not match the " +
                        "private key.", nameof(walletAddress));
                WalletAddress = _signer.Address;
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
                ? CowProtocolExtensions.ProbeAddress
                : walletAddress.NormalizeAddress();
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-CoW-Protocol-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "CoW_Protocol_JSON-RPC";

    public CowProtocolChains Chain { get; }

    public string WalletAddress { get; }

    public bool IsSigningAvailable => _signer is not null;

    public bool IsWalletConfigured =>
        !WalletAddress.EqualsIgnoreCase(CowProtocolExtensions.ProbeAddress);

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _requestGate.Dispose();
        _transactionGate.Dispose();
        if (_privateKey is not null)
            CryptographicOperations.ZeroMemory(_privateKey);
        base.DisposeManaged();
    }

    public async ValueTask VerifyAsync(CancellationToken cancellationToken)
    {
        var chainId = (await SendAsync<CowProtocolRpcEmptyParameters, string>(
            "eth_chainId", new(), true, cancellationToken)).ParseInteger();
        if (chainId != new BigInteger((int)Chain))
            throw new InvalidOperationException(
                $"JSON-RPC is connected to chain {chainId}, but CoW Protocol " +
                $"chain {(int)Chain} is configured.");

        await VerifyContractAsync(CowProtocolExtensions.SettlementAddress,
            "Settlement", cancellationToken);
        await VerifyContractAsync(CowProtocolExtensions.VaultRelayerAddress,
            "VaultRelayer", cancellationToken);

        var domain = (await CallContractAsync(
            CowProtocolExtensions.SettlementAddress,
            CowProtocolExtensions.EncodeStaticCall("domainSeparator()"),
            cancellationToken)).NormalizeBytes32();
        var expectedDomain = CowProtocolSigner.GetDomainSeparator(Chain);
        if (!domain.EqualsIgnoreCase(expectedDomain))
            throw new InvalidDataException(
                "The configured Settlement contract returned an unexpected " +
                "EIP-712 domain separator.");

        var relayer = CowProtocolExtensions.ReadAbiAddress(
            await CallContractAsync(CowProtocolExtensions.SettlementAddress,
                CowProtocolExtensions.EncodeStaticCall("vaultRelayer()"),
                cancellationToken), 0);
        if (!relayer.EqualsIgnoreCase(
            CowProtocolExtensions.VaultRelayerAddress))
            throw new InvalidDataException(
                "The configured Settlement contract returned an unexpected " +
                "VaultRelayer address.");
    }

    public async ValueTask<CowProtocolToken> GetTokenAsync(string address,
        CancellationToken cancellationToken)
    {
        address = address.NormalizeAddress();
        if (address.IsNativeToken())
        {
            var nativeSymbol = Chain.GetNativeSymbol();
            return new()
            {
                Address = address,
                Symbol = nativeSymbol,
                Name = nativeSymbol,
                Decimals = 18,
            };
        }
        await VerifyContractAsync(address, "ERC-20 token", cancellationToken);
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

    public async ValueTask<BigInteger> GetBalanceAsync(CowProtocolToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            return (await SendAsync<CowProtocolRpcAddressTagParameters, string>(
                "eth_getBalance", new()
                {
                    Address = WalletAddress,
                    BlockTag = "latest",
                }, true, cancellationToken)).ParseInteger();
        var data = CowProtocolExtensions.EncodeStaticCall(
            "balanceOf(address)",
            CowProtocolExtensions.AbiAddress(WalletAddress));
        return (await CallContractAsync(token.Address, data,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<BigInteger> GetAllowanceAsync(
        CowProtocolToken token, string spender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            return BigInteger.Pow(2, 256) - 1;
        var data = CowProtocolExtensions.EncodeStaticCall(
            "allowance(address,address)",
            CowProtocolExtensions.AbiAddress(WalletAddress),
            CowProtocolExtensions.AbiAddress(spender));
        return (await CallContractAsync(token.Address, data,
            cancellationToken)).ParseInteger();
    }

    public CowProtocolTransaction CreateApprovalTransaction(
        CowProtocolToken token, string spender, BigInteger amount)
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
            Data = CowProtocolExtensions.EncodeStaticCall(
                "approve(address,uint256)",
                CowProtocolExtensions.AbiAddress(spender),
                CowProtocolExtensions.AbiWord(amount)),
            Value = BigInteger.Zero,
        };
    }

    public CowProtocolSignedOrder SignOrder(CowProtocolOrderData order)
    {
        EnsureSigningAvailable();
        return _signer.SignOrder(order);
    }

    public string SignCancellation(string uid)
    {
        EnsureSigningAvailable();
        return _signer.SignCancellation(uid);
    }

    public string GetSpender() => CowProtocolExtensions.VaultRelayerAddress;

    public async ValueTask<BigInteger> EstimateGasAsync(
        CowProtocolTransaction transaction,
        CancellationToken cancellationToken)
    {
        ValidateTransaction(transaction);
        var call = ToRpcCall(transaction);
        return (await SendAsync<CowProtocolRpcCallOnlyParameters, string>(
            "eth_estimateGas", new() { Call = call }, true,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<string> SendTransactionAsync(
        CowProtocolTransaction transaction,
        CancellationToken cancellationToken)
    {
        EnsureSigningAvailable();
        ValidateTransaction(transaction);
        await _transactionGate.WaitAsync(cancellationToken);
        try
        {
            var nonce = (await SendAsync<CowProtocolRpcAddressTagParameters,
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
                var tx = new Transaction1559(new BigInteger((int)Chain), nonce,
                    eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
                    transaction.To.NormalizeAddress(), transaction.Value,
                    transaction.Data, null);
                new Transaction1559Signer().SignTransaction(_privateKey, tx);
                encoded = tx.GetRLPEncoded();
            }
            else
            {
                var gasPrice = (await SendAsync<
                    CowProtocolRpcEmptyParameters, string>("eth_gasPrice",
                    new(), true, cancellationToken)).ParseInteger();
                var tx = new LegacyTransactionChainId(
                    nonce.ToBytesForRLPEncoding(),
                    gasPrice.ToBytesForRLPEncoding(),
                    gasLimit.ToBytesForRLPEncoding(),
                    transaction.To.NormalizeAddress().HexToByteArray(),
                    transaction.Value.ToBytesForRLPEncoding(), data,
                    new BigInteger((int)Chain).ToBytesForRLPEncoding());
                new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
                encoded = tx.GetRLPEncoded();
            }
            var hash = await SendAsync<CowProtocolRpcValueParameters, string>(
                "eth_sendRawTransaction",
                new() { Value = encoded.ToHex(true) }, false,
                cancellationToken);
            return hash.NormalizeHash();
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public async ValueTask<CowProtocolRpcReceipt> GetReceiptAsync(string hash,
        CancellationToken cancellationToken)
    {
        hash = hash.NormalizeHash();
        var receipt = await SendAsync<CowProtocolRpcValueParameters,
            CowProtocolRpcReceipt>("eth_getTransactionReceipt", new()
            {
                Value = hash,
            }, true, cancellationToken);
        if (receipt is null)
            return null;
        if (!receipt.TransactionHash.IsEmpty() &&
            !receipt.TransactionHash.NormalizeHash().EqualsIgnoreCase(hash))
            throw new InvalidDataException(
                "JSON-RPC returned a receipt for a different transaction.");
        if (receipt.BlockNumber.IsEmpty())
            throw new InvalidDataException(
                $"Transaction '{hash}' receipt has no block number.");
        _ = receipt.BlockNumber.ParseInteger();
        return receipt;
    }

    public async ValueTask<CowProtocolRpcReceipt> WaitForReceiptAsync(
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

    public async ValueTask<BigInteger> GetLatestBlockNumberAsync(
        CancellationToken cancellationToken)
        => (await SendAsync<CowProtocolRpcEmptyParameters, string>(
            "eth_blockNumber", new(), true, cancellationToken)).ParseInteger();

    public async ValueTask<CowProtocolRpcBlock> GetBlockAsync(
        BigInteger blockNumber, CancellationToken cancellationToken)
    {
        if (blockNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(blockNumber));
        var block = await SendAsync<CowProtocolRpcTagBooleanParameters,
            CowProtocolRpcBlock>("eth_getBlockByNumber", new()
            {
                BlockTag = blockNumber.ToRpcHex(),
                IsTransactionsIncluded = false,
            }, true, cancellationToken);
        return block ?? throw new InvalidDataException(
            $"Block '{blockNumber}' was not found on chain {(int)Chain}.");
    }

    public async ValueTask<DateTime> GetBlockTimeAsync(
        BigInteger blockNumber, CancellationToken cancellationToken)
    {
        var block = await GetBlockAsync(blockNumber, cancellationToken);
        if (block.Timestamp.IsEmpty())
            throw new InvalidDataException(
                $"Block '{blockNumber}' has no timestamp.");
        return block.Timestamp.ParseInteger().ToUtcTime();
    }

    public async ValueTask<BigInteger> FindBlockAsync(DateTime time,
        BigInteger latestBlock, CancellationToken cancellationToken)
    {
        if (latestBlock < 0)
            throw new ArgumentOutOfRangeException(nameof(latestBlock));
        time = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
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

    public ValueTask<CowProtocolRpcLog[]> GetTradeLogsAsync(
        BigInteger fromBlock, BigInteger toBlock,
        CancellationToken cancellationToken)
    {
        if (fromBlock < 0 || toBlock < fromBlock)
            throw new ArgumentOutOfRangeException(nameof(fromBlock));
        return SendAsync<CowProtocolRpcLogsParameters, CowProtocolRpcLog[]>(
            "eth_getLogs", new()
            {
                Filter = new()
                {
                    FromBlock = fromBlock.ToRpcHex(),
                    ToBlock = toBlock.ToRpcHex(),
                    Address = CowProtocolExtensions.SettlementAddress,
                    Topics = [CowProtocolExtensions.TradeTopic],
                },
            }, true, cancellationToken);
    }

    private async ValueTask VerifyContractAsync(string address, string name,
        CancellationToken cancellationToken)
    {
        var code = await SendAsync<CowProtocolRpcAddressTagParameters, string>(
            "eth_getCode", new()
            {
                Address = address.NormalizeAddress(),
                BlockTag = "latest",
            }, true, cancellationToken);
        if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
            code.EqualsIgnoreCase("0x0"))
            throw new InvalidDataException(
                $"CoW Protocol {name} '{address}' is not a contract on " +
                $"chain {(int)Chain}.");
    }

    private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
        TryGetEip1559FeesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var block = await SendAsync<
                CowProtocolRpcTagBooleanParameters, CowProtocolRpcBlock>(
                "eth_getBlockByNumber", new()
                {
                    BlockTag = "latest",
                    IsTransactionsIncluded = false,
                }, true, cancellationToken);
            if (block?.BaseFeePerGas.IsEmpty() != false)
                return null;
            var baseFee = block.BaseFeePerGas.ParseInteger();
            var priority = (await SendAsync<
                CowProtocolRpcEmptyParameters, string>(
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
        => await SendAsync<CowProtocolRpcCallParameters, string>("eth_call",
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
        where TParameters : CowProtocolRpcParameters
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var payload = JsonConvert.SerializeObject(
            new CowProtocolRpcRequest<TParameters>
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
            CowProtocolRpcResponse<TResult> rpc;
            try
            {
                rpc = JsonConvert.DeserializeObject<
                    CowProtocolRpcResponse<TResult>>(body, _jsonSettings);
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

    private void ValidateTransaction(CowProtocolTransaction transaction)
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

    private CowProtocolRpcCall ToRpcCall(CowProtocolTransaction transaction)
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
                "An EVM private key is required for CoW Protocol orders.");
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
