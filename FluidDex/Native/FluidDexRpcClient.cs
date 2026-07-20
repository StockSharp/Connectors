namespace StockSharp.FluidDex.Native;

sealed class FluidDexRpcClient : BaseLogReceiver
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
	private readonly Lock _sync = new();
	private readonly Dictionary<string, FluidDexToken> _tokens =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly byte[] _privateKey;
	private readonly FluidDexChains _chain;
	private readonly BigInteger _chainId;
	private readonly string _factoryAddress;
	private readonly string _resolverAddress;
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

	public FluidDexRpcClient(string endpoint, FluidDexChains chain,
		string factoryAddress, string resolverAddress, string walletAddress,
		SecureString privateKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"EVM JSON-RPC endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_chain = chain;
		_chainId = chain.GetChainId();
		_factoryAddress = factoryAddress.NormalizeAddress();
		_resolverAddress = resolverAddress.NormalizeAddress();
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
				? FluidDexExtensions.NativeTokenAddress
				: walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-FluidDex-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "FluidDex_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsSigningAvailable => _privateKey is not null;

	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		FluidDexExtensions.NativeTokenAddress);

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
		var chainId = (await SendAsync<FluidDexRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != _chainId)
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but " +
				$"{_chain} chain {_chainId} is required.");
	}

	public async ValueTask<FluidDexToken> GetTokenAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		using (_sync.EnterScope())
			if (_tokens.TryGetValue(address, out var cached))
				return cached;
		FluidDexToken token;
		if (address.IsNativeToken())
		{
			var native = _chain.GetNativeToken();
			token = new()
			{
				Address = address,
				Symbol = native.Symbol,
				Name = native.Name,
				Decimals = 18,
			};
		}
		else
		{
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
				symbol = address[2..8].ToUpperInvariant();
			if (name.IsEmpty())
				name = symbol;
			token = new()
			{
				Address = address,
				Symbol = NormalizeSymbol(symbol),
				Name = name.Trim(),
				Decimals = (int)decimalsInteger,
			};
		}
		using (_sync.EnterScope())
		{
			_tokens.TryAdd(address, token);
			return _tokens[address];
		}
	}

	public async ValueTask<BigInteger> GetBalanceAsync(FluidDexToken token,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return (await SendAsync<FluidDexRpcAddressTagParameters,
				string>("eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<FluidDexPool> GetPoolAsync(string poolAddress,
		CancellationToken cancellationToken)
	{
		poolAddress = poolAddress.NormalizeAddress();
		var code = await SendAsync<FluidDexRpcAddressTagParameters, string>(
			"eth_getCode", new()
			{
				Address = poolAddress,
				BlockTag = "latest",
			}, true, cancellationToken);
		if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
			code.EqualsIgnoreCase("0x0"))
			throw new InvalidDataException(
				$"Fluid DEX pool '{poolAddress}' is not a contract.");
		var dexId = FluidDexExtensions.ReadAbiWord(
			await CallContractAsync(poolAddress,
				FluidDexExtensions.EncodeStaticCall("DEX_ID()"),
				cancellationToken), 0);
		if (dexId <= 0)
			throw new InvalidDataException(
				$"Fluid DEX pool '{poolAddress}' returned an invalid ID.");
		var officialAddress = FluidDexExtensions.ReadAbiAddress(
			await CallContractAsync(_factoryAddress,
				FluidDexExtensions.EncodeStaticCall("getDexAddress(uint256)",
					FluidDexExtensions.AbiWord(dexId)), cancellationToken), 0);
		if (!officialAddress.EqualsIgnoreCase(poolAddress))
			throw new InvalidDataException(
				$"Pool '{poolAddress}' was not created by the configured " +
				"Fluid DEX factory.");
		return await GetPoolByIdAsync(dexId, cancellationToken);
	}

	public async ValueTask<FluidDexPool[]> DiscoverPoolsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum <= 0)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var total = FluidDexExtensions.ReadAbiWord(
			await CallContractAsync(_resolverAddress,
				FluidDexExtensions.EncodeStaticCall("getTotalPools()"),
				cancellationToken), 0);
		if (total < 0 || total > int.MaxValue)
			throw new InvalidDataException(
				$"Fluid DEX resolver returned invalid pool count '{total}'.");
		var count = Math.Min((int)total, maximum);
		var result = new List<FluidDexPool>(count);
		for (var index = 1; index <= count; index++)
		{
			try
			{
				result.Add(await GetPoolByIdAsync(index, cancellationToken));
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				this.AddWarningLog(
					"Fluid DEX pool {0} discovery failed: {1}", index,
					error.Message);
			}
		}
		return [.. result];
	}

	private async ValueTask<FluidDexPool> GetPoolByIdAsync(BigInteger dexId,
		CancellationToken cancellationToken)
	{
		var response = await CallContractAsync(_resolverAddress,
			FluidDexExtensions.EncodeStaticCall("getPool(uint256)",
				FluidDexExtensions.AbiWord(dexId)), cancellationToken);
		var poolAddress = FluidDexExtensions.ReadAbiAddress(response, 0);
		var token0Address = FluidDexExtensions.ReadAbiAddress(response, 1);
		var token1Address = FluidDexExtensions.ReadAbiAddress(response, 2);
		var fee = FluidDexExtensions.ReadAbiWord(response, 3);
		if (poolAddress.EqualsIgnoreCase(
			"0x0000000000000000000000000000000000000000") ||
			token0Address.EqualsIgnoreCase(token1Address) ||
			fee < 0 || fee > 1_000_000)
			throw new InvalidDataException(
				$"Fluid DEX pool {dexId} returned invalid metadata.");
		return new()
		{
			PoolId = poolAddress,
			DexId = dexId,
			Fee = fee,
			Token0 = await GetTokenAsync(token0Address, cancellationToken),
			Token1 = await GetTokenAsync(token1Address, cancellationToken),
		};
	}

	public async ValueTask<FluidDexQuote> GetQuoteAsync(
		FluidDexMarket market, FluidDexTradeTypes tradeType,
		BigInteger amount, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == FluidDexTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		var swap0To1 = tokenIn.EqualsIgnoreCase(market.Token0.Address);
		if (!swap0To1 && !tokenIn.EqualsIgnoreCase(market.Token1.Address) ||
			(tokenOut.EqualsIgnoreCase(tokenIn)))
			throw new InvalidDataException(
				"Fluid DEX market token orientation is invalid.");
		var data = isExactInput
			? FluidDexExtensions.EncodeStaticCall(
				"estimateSwapIn(address,bool,uint256,uint256)",
				FluidDexExtensions.AbiAddress(market.PoolId),
				FluidDexExtensions.AbiBoolean(swap0To1),
				FluidDexExtensions.AbiWord(amount),
				FluidDexExtensions.AbiWord(BigInteger.Zero))
			: FluidDexExtensions.EncodeStaticCall(
				"estimateSwapOut(address,bool,uint256,uint256)",
				FluidDexExtensions.AbiAddress(market.PoolId),
				FluidDexExtensions.AbiBoolean(swap0To1),
				FluidDexExtensions.AbiWord(amount),
				FluidDexExtensions.AbiWord(BigInteger.Pow(2, 256) - 1));
		var quoted = FluidDexExtensions.ReadAbiWord(
			await CallContractAsync(_resolverAddress, data,
				cancellationToken), 0);
		if (quoted <= 0)
			throw new InvalidDataException(
				"Fluid DEX resolver returned a non-positive quote amount.");
		return new()
		{
			InputAmount = isExactInput ? amount : quoted,
			OutputAmount = isExactInput ? quoted : amount,
		};
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(
		FluidDexToken token, string spender,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return BigInteger.Pow(2, 256) - 1;
		var data = FluidDexExtensions.EncodeStaticCall(
			"allowance(address,address)",
			FluidDexExtensions.AbiAddress(WalletAddress),
			FluidDexExtensions.AbiAddress(spender));
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public FluidDexTransaction CreateApprovalTransaction(
		FluidDexToken token, string spender, BigInteger amount)
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
			Data = FluidDexExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				FluidDexExtensions.AbiAddress(spender),
				FluidDexExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public FluidDexTransaction CreateSwapTransaction(
		FluidDexMarket market, FluidDexTradeTypes tradeType,
		BigInteger amount, FluidDexQuote quote, decimal slippageTolerance)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (amount <= 0 || quote.InputAmount <= 0 ||
			quote.OutputAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == FluidDexTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		var slippageBps = new BigInteger(slippageTolerance * 100m);
		var minimumOutput = quote.OutputAmount *
			(10_000 - slippageBps) / 10_000;
		var maximumInput = (quote.InputAmount *
			(10_000 + slippageBps) + 9_999) / 10_000;
		if (minimumOutput <= 0 || maximumInput <= 0)
			throw new InvalidOperationException(
				"Slippage-adjusted Fluid DEX amount is non-positive.");
		var swap0To1 = tokenIn.EqualsIgnoreCase(market.Token0.Address);
		var data = isExactInput
			? FluidDexExtensions.EncodeStaticCall(
				"swapIn(bool,uint256,uint256,address)",
				FluidDexExtensions.AbiBoolean(swap0To1),
				FluidDexExtensions.AbiWord(amount),
				FluidDexExtensions.AbiWord(minimumOutput),
				FluidDexExtensions.AbiAddress(WalletAddress))
			: FluidDexExtensions.EncodeStaticCall(
				"swapOut(bool,uint256,uint256,address)",
				FluidDexExtensions.AbiBoolean(swap0To1),
				FluidDexExtensions.AbiWord(amount),
				FluidDexExtensions.AbiWord(maximumInput),
				FluidDexExtensions.AbiAddress(WalletAddress));
		return new()
		{
			To = market.PoolId.NormalizeAddress(),
			Data = data,
			Value = tokenIn.IsNativeToken()
				? isExactInput ? amount : maximumInput
				: BigInteger.Zero,
		};
	}

	public string GetSpender(FluidDexMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return market.PoolId.NormalizeAddress();
	}

	public async ValueTask<BigInteger> EstimateGasAsync(
		FluidDexTransaction transaction,
		CancellationToken cancellationToken)
	{
		var call = ToRpcCall(transaction);
		return (await SendAsync<FluidDexRpcCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = call }, true,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<string> SendTransactionAsync(
		FluidDexTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<FluidDexRpcAddressTagParameters,
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
					_chainId, nonce,
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
					FluidDexRpcEmptyParameters, string>("eth_gasPrice",
					new(), true, cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					_chainId.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey,
					tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<FluidDexRpcValueParameters, string>(
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

	public ValueTask<FluidDexRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<FluidDexRpcValueParameters, FluidDexRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = NormalizeHash(hash),
			}, true, cancellationToken);

	public async ValueTask<BigInteger> GetLatestBlockNumberAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<FluidDexRpcEmptyParameters, string>(
			"eth_blockNumber", new(), true, cancellationToken)).ParseInteger();

	public async ValueTask<FluidDexRpcBlock> GetBlockAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<FluidDexRpcTagBooleanParameters,
			FluidDexRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken);
		return block ?? throw new InvalidDataException(
			$"{_chain} block '{blockNumber}' was not found.");
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		var block = await GetBlockAsync(blockNumber, cancellationToken);
		if (block.Timestamp.IsEmpty())
			throw new InvalidDataException(
				$"{_chain} block '{blockNumber}' has no timestamp.");
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

	public ValueTask<FluidDexRpcLog[]> GetLogsAsync(FluidDexMarket market,
		BigInteger fromBlock, BigInteger toBlock,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (fromBlock < 0 || toBlock < fromBlock)
			throw new ArgumentOutOfRangeException(nameof(fromBlock));
		return SendAsync<FluidDexRpcLogsParameters, FluidDexRpcLog[]>(
			"eth_getLogs", new()
			{
				Filter = new()
				{
					FromBlock = fromBlock.ToRpcHex(),
					ToBlock = toBlock.ToRpcHex(),
					Address = market.PoolId,
					Topics = [FluidDexExtensions.SwapTopic],
				},
			}, true, cancellationToken);
	}

	public async ValueTask<FluidDexRpcReceipt> WaitForReceiptAsync(
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
				FluidDexRpcTagBooleanParameters, FluidDexRpcBlock>(
				"eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<
				FluidDexRpcEmptyParameters, string>(
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
		=> await SendAsync<FluidDexRpcCallParameters, string>("eth_call",
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
		where TParameters : FluidDexRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new FluidDexRpcRequest<TParameters>
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
			FluidDexRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					FluidDexRpcResponse<TResult>>(body, _jsonSettings);
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

	private void ValidateTransaction(FluidDexTransaction transaction)
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

	private FluidDexRpcCall ToRpcCall(FluidDexTransaction transaction)
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
				"An EVM private key is required for FluidDex transactions.");
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
