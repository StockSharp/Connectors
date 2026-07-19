namespace StockSharp.Uniswap.Native;

sealed class UniswapRpcClient : BaseLogReceiver
{
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly UniswapChains _chain;
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

    public UniswapRpcClient(string endpoint, UniswapChains chain,
        string walletAddress, SecureString privateKey)
    {
        endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
            !(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
                _endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
            throw new ArgumentException(
                "EVM JSON-RPC endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(endpoint));
        _chain = chain;
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
            WalletAddress = walletAddress.NormalizeAddress();
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Uniswap-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Uniswap_JSON-RPC";

    public string WalletAddress { get; }

    public bool IsSigningAvailable => _privateKey is not null;

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
        var chainId = (await SendAsync<UniswapRpcEmptyParameters, string>(
            "eth_chainId", new(), true, cancellationToken)).ParseInteger();
        if (chainId != new BigInteger((int)_chain))
            throw new InvalidOperationException(
                $"JSON-RPC is connected to chain {chainId}, but " +
                $"{(int)_chain} ({_chain}) is configured.");
    }

    public async ValueTask<UniswapToken> GetTokenAsync(string address,
        CancellationToken cancellationToken)
    {
        address = address.NormalizeAddress();
        if (address.IsNativeToken())
        {
            if (!_chain.HasNativeToken())
                throw new NotSupportedException(
                    $"{_chain} has no native token.");
            return new()
            {
                Address = address,
                Symbol = _chain.NativeSymbol(),
                Name = _chain.NativeSymbol(),
                Decimals = _chain.NativeDecimals(),
            };
        }
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
        if (symbol.IsEmpty())
            symbol = $"{address[2..8].ToUpperInvariant()}";
        if (name.IsEmpty())
            name = symbol;
        return new()
        {
            Address = address,
            Symbol = NormalizeSymbol(symbol),
            Name = name.Trim(),
            Decimals = (int)decimalsInteger,
        };
    }

    public async ValueTask<BigInteger> GetBalanceAsync(UniswapToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Address.IsNativeToken())
            return (await SendAsync<UniswapRpcAddressTagParameters, string>(
                "eth_getBalance", new()
                {
                    Address = WalletAddress,
                    BlockTag = "latest",
                }, true, cancellationToken)).ParseInteger();
        var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
        return (await CallContractAsync(token.Address, data,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<BigInteger> EstimateGasAsync(
        UniswapTransactionRequest transaction,
        CancellationToken cancellationToken)
    {
        var call = ToRpcCall(transaction, null);
        return (await SendAsync<UniswapRpcCallOnlyParameters, string>(
            "eth_estimateGas", new() { Call = call }, true,
            cancellationToken)).ParseInteger();
    }

    public async ValueTask<string> SendTransactionAsync(
        UniswapTransactionRequest transaction,
        CancellationToken cancellationToken)
    {
        EnsureSigningAvailable();
        ValidateTransaction(transaction);
        await _transactionGate.WaitAsync(cancellationToken);
        try
        {
            var nonce = (await SendAsync<UniswapRpcAddressTagParameters,
                string>("eth_getTransactionCount", new()
                {
                    Address = WalletAddress,
                    BlockTag = "pending",
                }, true, cancellationToken)).ParseInteger();
            var gasLimit = transaction.GasLimit.IsEmpty()
                ? await EstimateGasAsync(transaction, cancellationToken)
                : transaction.GasLimit.ParseInteger();
            if (gasLimit <= 0)
                throw new InvalidDataException(
                    "The transaction gas limit must be positive.");
            var value = transaction.Value.IsEmpty()
                ? BigInteger.Zero
                : transaction.Value.ParseInteger();
            var data = transaction.Data[2..].HexToByteArray();
            byte[] encoded;
            if (!transaction.MaximumFeePerGas.IsEmpty() ||
                !transaction.MaximumPriorityFeePerGas.IsEmpty())
            {
                if (transaction.MaximumFeePerGas.IsEmpty() ||
                    transaction.MaximumPriorityFeePerGas.IsEmpty())
                    throw new InvalidDataException(
                        "Both EIP-1559 fee fields must be present.");
                if (!transaction.GasPrice.IsEmpty())
                    throw new InvalidDataException(
                        "A transaction cannot contain both legacy and " +
                        "EIP-1559 gas prices.");
                var tx = new Transaction1559(
                    new BigInteger((int)_chain), nonce,
                    transaction.MaximumPriorityFeePerGas.ParseInteger(),
                    transaction.MaximumFeePerGas.ParseInteger(), gasLimit,
                    transaction.To.NormalizeAddress(), value,
                    transaction.Data, null);
                new Transaction1559Signer().SignTransaction(_privateKey, tx);
                encoded = tx.GetRLPEncoded();
            }
            else
            {
                var gasPrice = transaction.GasPrice.IsEmpty()
                    ? (await SendAsync<UniswapRpcEmptyParameters, string>(
                        "eth_gasPrice", new(), true,
                        cancellationToken)).ParseInteger()
                    : transaction.GasPrice.ParseInteger();
                var tx = new LegacyTransactionChainId(
                    nonce.ToBytesForRLPEncoding(),
                    gasPrice.ToBytesForRLPEncoding(),
                    gasLimit.ToBytesForRLPEncoding(),
                    transaction.To.NormalizeAddress().HexToByteArray(),
                    value.ToBytesForRLPEncoding(), data,
                    new BigInteger((int)_chain).ToBytesForRLPEncoding());
                new LegacyTransactionSigner().SignTransaction(_privateKey,
                    tx);
                encoded = tx.GetRLPEncoded();
            }
            var raw = encoded.ToHex(true);
            var hash = await SendAsync<UniswapRpcValueParameters, string>(
                "eth_sendRawTransaction", new() { Value = raw }, false,
                cancellationToken);
            return NormalizeHash(hash);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public ValueTask<UniswapRpcReceipt> GetReceiptAsync(string hash,
        CancellationToken cancellationToken)
        => SendAsync<UniswapRpcValueParameters, UniswapRpcReceipt>(
            "eth_getTransactionReceipt", new()
            {
                Value = NormalizeHash(hash),
            }, true, cancellationToken);

    public async ValueTask<UniswapRpcReceipt> WaitForReceiptAsync(
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

    private async ValueTask<string> CallContractAsync(string address,
        string data, CancellationToken cancellationToken)
        => await SendAsync<UniswapRpcCallParameters, string>("eth_call",
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
        where TParameters : UniswapRpcParameters
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var payload = JsonConvert.SerializeObject(
            new UniswapRpcRequest<TParameters>
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
            UniswapRpcResponse<TResult> rpc;
            try
            {
                rpc = JsonConvert.DeserializeObject<
                    UniswapRpcResponse<TResult>>(body, _jsonSettings);
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

    private void ValidateTransaction(UniswapTransactionRequest transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.ChainId != (int)_chain)
            throw new InvalidDataException(
                $"Transaction chain {transaction.ChainId} does not match " +
                $"configured chain {(int)_chain}.");
        if (!transaction.From.NormalizeAddress()
            .EqualsIgnoreCase(WalletAddress))
            throw new InvalidDataException(
                "Transaction sender does not match the configured wallet.");
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
        if (!transaction.Value.IsEmpty() &&
            transaction.Value.ParseInteger() < 0)
            throw new InvalidDataException(
                "Transaction value cannot be negative.");
    }

    private static UniswapRpcCall ToRpcCall(
        UniswapTransactionRequest transaction, string gas)
        => new()
        {
            From = transaction.From.NormalizeAddress(),
            To = transaction.To.NormalizeAddress(),
            Data = transaction.Data,
            Value = transaction.Value.IsEmpty()
                ? BigInteger.Zero.ToRpcHex()
                : transaction.Value.ParseInteger().ToRpcHex(),
            Gas = gas,
            GasPrice = transaction.GasPrice.IsEmpty()
                ? null
                : transaction.GasPrice.ParseInteger().ToRpcHex(),
            MaximumFeePerGas = transaction.MaximumFeePerGas.IsEmpty()
                ? null
                : transaction.MaximumFeePerGas.ParseInteger().ToRpcHex(),
            MaximumPriorityFeePerGas =
                transaction.MaximumPriorityFeePerGas.IsEmpty()
                    ? null
                    : transaction.MaximumPriorityFeePerGas.ParseInteger()
                        .ToRpcHex(),
        };

    private void EnsureSigningAvailable()
    {
        if (!IsSigningAvailable)
            throw new InvalidOperationException(
                "An EVM private key is required for Uniswap transactions.");
    }

    private static string NormalizeSymbol(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return value;
        var result = new string(value.Where(static ch =>
            char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-').ToArray());
        return result.IsEmpty() ? value : result.ToUpperInvariant();
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
        return value.IsEmpty() ? "request rejected"
            : value.Length <= 512 ? value : value[..512];
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
