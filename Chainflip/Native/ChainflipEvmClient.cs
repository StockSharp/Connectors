namespace StockSharp.Chainflip.Native;

sealed class ChainflipEvmClient : BaseLogReceiver
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

	public ChainflipEvmClient(string endpoint, string chain,
		string walletAddress, SecureString privateKey)
	{
		Chain = chain.ThrowIfEmpty(nameof(chain)).Trim();
		ChainId = Chain.GetEvmChainId();
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
				if (_privateKey.Length != 32)
					throw new ArgumentException(
						"An EVM private key must contain exactly 32 bytes.");
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
				? ChainflipExtensions.ProbeAddress
				: walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Chainflip-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => $"Chainflip_{Chain}_JSON_RPC";

	public string Chain { get; }
	public int ChainId { get; }
	public string WalletAddress { get; }
	public bool IsSigningAvailable => _privateKey is not null;
	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		ChainflipExtensions.ProbeAddress);

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
		var chainId = (await SendAsync<ChainflipEvmEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(ChainId))
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but Chainflip " +
					$"{Chain} chain {ChainId} is configured.");
	}

	public async ValueTask<BigInteger> GetBalanceAsync(ChainflipAsset asset,
		CancellationToken cancellationToken)
	{
		ValidateAsset(asset);
		if (asset.IsNative)
			return (await SendAsync<ChainflipEvmAddressTagParameters,
				string>("eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = ChainflipExtensions.EncodeStaticCall(
			"balanceOf(address)",
			ChainflipExtensions.AbiAddress(WalletAddress));
		return (await CallContractAsync(asset.ContractAddress, data,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(
		ChainflipAsset asset, string spender,
		CancellationToken cancellationToken)
	{
		ValidateAsset(asset);
		if (asset.IsNative)
			return BigInteger.Pow(2, 256) - 1;
		var data = ChainflipExtensions.EncodeStaticCall(
			"allowance(address,address)",
			ChainflipExtensions.AbiAddress(WalletAddress),
			ChainflipExtensions.AbiAddress(spender));
		return (await CallContractAsync(asset.ContractAddress, data,
			cancellationToken)).ParseInteger();
	}

	public ChainflipTransaction CreateApprovalTransaction(
		ChainflipAsset asset, string spender, BigInteger amount)
	{
		ValidateAsset(asset);
		if (asset.IsNative)
			throw new NotSupportedException(
				"A native asset does not use ERC-20 approvals.");
		if (amount < 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return new()
		{
			To = asset.ContractAddress,
			Data = ChainflipExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				ChainflipExtensions.AbiAddress(spender),
				ChainflipExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public async ValueTask<string> SendTransactionAsync(
		ChainflipTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<
				ChainflipEvmAddressTagParameters, string>(
				"eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger();
			if (nonce < 0)
				throw new InvalidDataException(
					"The pending transaction nonce cannot be negative.");
			var estimated = await EstimateGasAsync(transaction,
				cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"The transaction gas estimate must be positive.");
			var gasLimit = BigInteger.Max((estimated * 120 + 99) / 100,
				transaction.SuggestedGas);
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(new BigInteger(ChainId), nonce,
					eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<
					ChainflipEvmEmptyParameters, string>("eth_gasPrice",
					new(), true, cancellationToken)).ParseInteger();
				if (gasPrice <= 0)
					throw new InvalidDataException(
						"The legacy gas price must be positive.");
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(ChainId).ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<ChainflipEvmValueParameters, string>(
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

	public async ValueTask<ChainflipEvmReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
	{
		hash = hash.NormalizeHash();
		var receipt = await SendAsync<ChainflipEvmValueParameters,
			ChainflipEvmReceipt>("eth_getTransactionReceipt", new()
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

	public async ValueTask<ChainflipEvmReceipt> WaitForReceiptAsync(
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

	public async ValueTask<DateTime> GetBlockTimeAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<ChainflipEvmTagBooleanParameters,
			ChainflipEvmBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken);
		if (block?.Timestamp.IsEmpty() != false)
			throw new InvalidDataException(
				$"Block '{blockNumber}' has no timestamp.");
		return ((long)block.Timestamp.ParseInteger()).ToUtcTime();
	}

	private async ValueTask<BigInteger> EstimateGasAsync(
		ChainflipTransaction transaction,
		CancellationToken cancellationToken)
	{
		var call = ToRpcCall(transaction);
		return (await SendAsync<ChainflipEvmCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = call }, true,
			cancellationToken)).ParseInteger();
	}

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<
				ChainflipEvmTagBooleanParameters, ChainflipEvmBlock>(
				"eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<
				ChainflipEvmEmptyParameters, string>(
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
		=> await SendAsync<ChainflipEvmCallParameters, string>("eth_call",
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

	private async ValueTask<TResult> SendAsync<TParameters, TResult>(
		string method, TParameters parameters, bool isRead,
		CancellationToken cancellationToken)
		where TParameters : ChainflipEvmParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new ChainflipEvmRequest<TParameters>
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
					$"EVM JSON-RPC HTTP {(int)response.StatusCode}: " +
						Truncate(body));
			ChainflipEvmResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					ChainflipEvmResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"EVM JSON-RPC returned an unexpected response.", error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(
					"EVM JSON-RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(
					$"EVM JSON-RPC {rpc.Error.Code}: " +
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

	private void ValidateAsset(ChainflipAsset asset)
	{
		ArgumentNullException.ThrowIfNull(asset);
		if (!asset.IsEvm || !asset.Chain.EqualsIgnoreCase(Chain))
			throw new ArgumentException(
				$"Asset '{asset.Key}' does not belong to EVM chain " +
					$"'{Chain}'.", nameof(asset));
	}

	private static void ValidateTransaction(ChainflipTransaction transaction)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		_ = transaction.To.NormalizeAddress();
		_ = transaction.Data.NormalizeData();
		if (transaction.Value < 0 || transaction.SuggestedGas < 0)
			throw new InvalidDataException(
				"Transaction value and suggested gas cannot be negative.");
	}

	private ChainflipEvmCall ToRpcCall(ChainflipTransaction transaction)
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
				"An EVM private key is required for Chainflip vault swaps.");
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"EVM JSON-RPC response exceeds the 8 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"EVM JSON-RPC response exceeds the 8 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}
}
