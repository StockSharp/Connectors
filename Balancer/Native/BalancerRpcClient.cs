namespace StockSharp.Balancer.Native;

sealed class BalancerRpcClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly BalancerDeployment _deployment;
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

	public BalancerRpcClient(BalancerDeployment deployment, string endpoint,
		string walletAddress, SecureString privateKey)
	{
		_deployment = deployment ?? throw new ArgumentNullException(
			nameof(deployment));
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
					!derived.EqualsIgnoreCase(walletAddress.NormalizeAddress()))
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
			WalletAddress = walletAddress.IsEmpty()
				? BalancerExtensions.ZeroAddress
				: walletAddress.NormalizeAddress();
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Balancer-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Balancer_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsSigningAvailable => _privateKey is not null;

	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		BalancerExtensions.ZeroAddress);

	public async ValueTask VerifyChainAsync(
		CancellationToken cancellationToken)
	{
		var chainId = (await SendAsync<BalancerRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(_deployment.ChainId))
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but " +
				$"{_deployment.Name} chain {_deployment.ChainId} is required.");
	}

	public async ValueTask VerifyContractAsync(string address, string name,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		var code = await SendAsync<BalancerRpcAddressTagParameters, string>(
			"eth_getCode", new()
			{
				Address = address,
				BlockTag = "latest",
			}, true, cancellationToken);
		if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
			code.EqualsIgnoreCase("0x0"))
			throw new InvalidDataException(
				$"{name.ThrowIfEmpty(nameof(name))} '{address}' is not a " +
				$"contract on {_deployment.Name}.");
	}

	public async ValueTask<BalancerToken> VerifyTokenAsync(
		BalancerToken source, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		var address = source.Address.NormalizeAddress();
		await VerifyContractAsync(address, "Balancer token", cancellationToken);
		var decimals = BalancerExtensions.ReadAbiWord(
			await CallContractAsync(address, "0x313ce567", cancellationToken), 0);
		if (decimals < 0 || decimals > 255 || source.Decimals != (int)decimals)
			throw new InvalidDataException(
				$"Balancer API decimals for token '{address}' do not match " +
				"the on-chain contract.");
		return new()
		{
			Address = address,
			Symbol = source.Symbol,
			Name = source.Name,
			Decimals = source.Decimals,
			Index = source.Index,
		};
	}

	public BalancerToken CreateNativeToken()
		=> new()
		{
			Address = BalancerExtensions.ZeroAddress,
			Symbol = _deployment.NativeSymbol,
			Name = _deployment.NativeSymbol,
			Decimals = 18,
			Index = -1,
		};

	public async ValueTask<BigInteger> GetBalanceAsync(BalancerToken token,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.EqualsIgnoreCase(BalancerExtensions.ZeroAddress))
			return (await SendAsync<BalancerRpcAddressTagParameters, string>(
				"eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = BalancerExtensions.EncodeStaticCall("balanceOf(address)",
			BalancerExtensions.AbiAddress(WalletAddress));
		return BalancerExtensions.ReadAbiWord(await CallContractAsync(
			token.Address, data, cancellationToken), 0);
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(BalancerToken token,
		string spender, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.EqualsIgnoreCase(BalancerExtensions.ZeroAddress))
			return BigInteger.Pow(2, 256) - 1;
		var data = BalancerExtensions.EncodeStaticCall(
			"allowance(address,address)",
			BalancerExtensions.AbiAddress(WalletAddress),
			BalancerExtensions.AbiAddress(spender));
		return BalancerExtensions.ReadAbiWord(await CallContractAsync(
			token.Address, data, cancellationToken), 0);
	}

	public async ValueTask<BigInteger> GetPermit2AllowanceAsync(
		BalancerToken token, string router,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		var data = BalancerExtensions.EncodeStaticCall(
			"allowance(address,address,address)",
			BalancerExtensions.AbiAddress(WalletAddress),
			BalancerExtensions.AbiAddress(token.Address),
			BalancerExtensions.AbiAddress(router));
		var response = await CallContractAsync(BalancerExtensions.Permit2Address,
			data, cancellationToken);
		var amount = BalancerExtensions.ReadAbiWord(response, 0);
		var expiration = BalancerExtensions.ReadAbiWord(response, 1);
		return expiration <= DateTime.UtcNow.ToUnixSeconds()
			? BigInteger.Zero
			: amount;
	}

	public async ValueTask VerifyPermit2Async(string router,
		CancellationToken cancellationToken)
	{
		await VerifyContractAsync(BalancerExtensions.Permit2Address,
			"Permit2", cancellationToken);
		var response = await CallContractAsync(router,
			BalancerExtensions.EncodeStaticCall("getPermit2()"),
			cancellationToken);
		var address = BalancerExtensions.ReadAbiAddress(response, 0);
		if (!address.EqualsIgnoreCase(BalancerExtensions.Permit2Address))
			throw new InvalidDataException(
				$"Balancer V3 Router '{router}' returned an unexpected " +
				"Permit2 contract.");
	}

	public BalancerTransaction CreateApprovalTransaction(BalancerToken token,
		string spender, BigInteger amount)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.EqualsIgnoreCase(BalancerExtensions.ZeroAddress))
			throw new NotSupportedException(
				"A native token does not use ERC-20 approvals.");
		if (amount < 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return new()
		{
			To = token.Address.NormalizeAddress(),
			Data = BalancerExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				BalancerExtensions.AbiAddress(spender),
				BalancerExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public BalancerTransaction CreatePermit2ApprovalTransaction(
		BalancerToken token, string router)
	{
		ArgumentNullException.ThrowIfNull(token);
		return new()
		{
			To = BalancerExtensions.Permit2Address,
			Data = BalancerExtensions.EncodeStaticCall(
				"approve(address,address,uint160,uint48)",
				BalancerExtensions.AbiAddress(token.Address),
				BalancerExtensions.AbiAddress(router),
				BalancerExtensions.AbiWord(BigInteger.Pow(2, 160) - 1),
				BalancerExtensions.AbiWord(BigInteger.Pow(2, 48) - 1)),
			Value = BigInteger.Zero,
		};
	}

	public BalancerTransaction CreateSwapTransaction(BalancerMarket market,
		BalancerSwapTypes swapType, BalancerQuote quote,
		decimal slippageTolerance, DateTime deadline)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (quote.SwapType != swapType)
			throw new InvalidDataException(
				"Balancer quote swap type does not match the transaction.");
		if (quote.InputAmount <= 0 || quote.OutputAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(quote));
		if (slippageTolerance is <= 0 or > 50)
			throw new ArgumentOutOfRangeException(nameof(slippageTolerance));
		var basisPoints = new BigInteger(slippageTolerance * 100m);
		BigInteger amount;
		BigInteger limit;
		if (swapType == BalancerSwapTypes.ExactIn)
		{
			amount = quote.InputAmount;
			limit = quote.OutputAmount * (10_000 - basisPoints) / 10_000;
		}
		else
		{
			amount = quote.OutputAmount;
			limit = (quote.InputAmount * (10_000 + basisPoints) + 9_999) /
				10_000;
		}
		if (amount <= 0 || limit <= 0)
			throw new InvalidOperationException(
				"Slippage-adjusted Balancer amount is non-positive.");
		var deadlineSeconds = deadline.ToUnixSeconds();
		if (deadlineSeconds <= DateTime.UtcNow.ToUnixSeconds())
			throw new ArgumentOutOfRangeException(nameof(deadline));
		return market.Pool.ProtocolVersion switch
		{
			2 => new()
			{
				To = _deployment.V2Vault,
				Data = BalancerExtensions.EncodeV2Swap(market, swapType,
					amount, limit, deadlineSeconds, WalletAddress),
				Value = BigInteger.Zero,
			},
			3 when !_deployment.V3Router.IsEmpty() => new()
			{
				To = _deployment.V3Router,
				Data = BalancerExtensions.EncodeV3Swap(market, swapType,
					amount, limit, deadlineSeconds),
				Value = BigInteger.Zero,
			},
			_ => throw new NotSupportedException(
				$"Balancer protocol V{market.Pool.ProtocolVersion} trading is " +
				$"not deployed on {_deployment.Name}."),
		};
	}

	public string GetSpender(BalancerMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return market.Pool.ProtocolVersion == 2
			? _deployment.V2Vault
			: BalancerExtensions.Permit2Address;
	}

	public string GetPermit2Spender(BalancerMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (market.Pool.ProtocolVersion != 3 || _deployment.V3Router.IsEmpty())
			throw new NotSupportedException(
				"Permit2 allowance is only used by Balancer V3 Router.");
		return _deployment.V3Router;
	}

	public async ValueTask<BigInteger> EstimateGasAsync(
		BalancerTransaction transaction, CancellationToken cancellationToken)
		=> (await SendAsync<BalancerRpcCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = ToRpcCall(transaction) }, true,
			cancellationToken)).ParseInteger();

	public async ValueTask<string> SendTransactionAsync(
		BalancerTransaction transaction, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<BalancerRpcAddressTagParameters,
				string>("eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger();
			var estimated = await EstimateGasAsync(transaction, cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"The transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(new BigInteger(_deployment.ChainId),
					nonce, eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<BalancerRpcEmptyParameters,
					string>("eth_gasPrice", new(), true,
					cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(), gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(_deployment.ChainId).ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			return (await SendAsync<BalancerRpcValueParameters, string>(
				"eth_sendRawTransaction", new() { Value = encoded.ToHex(true) },
				false, cancellationToken)).NormalizeHash();
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	public ValueTask<BalancerRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<BalancerRpcValueParameters, BalancerRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<BalancerRpcBlock> GetBlockAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		return await SendAsync<BalancerRpcTagBooleanParameters,
			BalancerRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken) ?? throw new InvalidDataException(
			$"{_deployment.Name} block '{blockNumber}' was not found.");
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(BigInteger blockNumber,
		CancellationToken cancellationToken)
	{
		var block = await GetBlockAsync(blockNumber, cancellationToken);
		if (block.Timestamp.IsEmpty())
			throw new InvalidDataException(
				$"{_deployment.Name} block '{blockNumber}' has no timestamp.");
		return block.Timestamp.ParseInteger().ToUtcTime();
	}

	public async ValueTask<BalancerRpcReceipt> WaitForReceiptAsync(string hash,
		TimeSpan timeout, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var receipt = await GetReceiptAsync(hash, cancellationToken);
			if (receipt is not null)
				return receipt;
			await Task.Delay((deadline - DateTime.UtcNow).Min(
				TimeSpan.FromSeconds(2)), cancellationToken);
		}
		throw new TimeoutException(
			$"Transaction '{hash}' was not mined within {timeout}.");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		_transactionGate.Dispose();
		if (_privateKey is not null)
			CryptographicOperations.ZeroMemory(_privateKey);
		base.DisposeManaged();
	}

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<BalancerRpcTagBooleanParameters,
				BalancerRpcBlock>("eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<BalancerRpcEmptyParameters,
				string>("eth_maxPriorityFeePerGas", new(), true,
				cancellationToken)).ParseInteger();
			return baseFee <= 0 || priority < 0
				? null
				: (priority, baseFee * 2 + priority);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			this.AddDebugLog(
				"EIP-1559 fee discovery failed; using legacy gas price: {0}",
				error.Message);
			return null;
		}
	}

	private async ValueTask<string> CallContractAsync(string address,
		string data, CancellationToken cancellationToken)
		=> await SendAsync<BalancerRpcCallParameters, string>("eth_call",
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
		where TParameters : BalancerRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new BalancerRpcRequest<TParameters>
			{
				Id = requestId,
				Method = method,
				Parameters = parameters,
			}, _jsonSettings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
			{
				Content = new StringContent(payload, Encoding.UTF8,
					"application/json"),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (isRead && attempt < 3 && (response.StatusCode ==
				(HttpStatusCode)429 || (int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"JSON-RPC HTTP {(int)response.StatusCode}: {Truncate(body)}");
			BalancerRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					BalancerRpcResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"JSON-RPC returned an unexpected response shape.", error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(
					"JSON-RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException($"JSON-RPC {rpc.Error.Code}: " +
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

	private void ValidateTransaction(BalancerTransaction transaction)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		_ = transaction.To.NormalizeAddress();
		if (transaction.Data.IsEmpty() ||
			!transaction.Data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			transaction.Data.Length <= 2 ||
			transaction.Data[2..].Any(static ch => !Uri.IsHexDigit(ch)) ||
			(transaction.Data.Length - 2) % 2 != 0)
			throw new InvalidDataException(
				"Transaction calldata must be a non-empty even-length hex string.");
		if (transaction.Value < 0)
			throw new InvalidDataException(
				"Transaction value cannot be negative.");
	}

	private BalancerRpcCall ToRpcCall(BalancerTransaction transaction)
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
				"An EVM private key is required for Balancer transactions.");
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
