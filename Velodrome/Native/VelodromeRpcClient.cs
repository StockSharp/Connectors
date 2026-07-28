namespace StockSharp.Velodrome.Native;

sealed class VelodromeRpcClient : BaseLogReceiver
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

	public VelodromeRpcClient(string endpoint, string walletAddress,
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
				? VelodromeExtensions.NativeTokenAddress
				: walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Velodrome-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Velodrome_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsSigningAvailable => _privateKey is not null;

	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		VelodromeExtensions.NativeTokenAddress);

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
		var chainId = (await SendAsync<VelodromeRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(VelodromeExtensions.ChainId))
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but " +
				$"Optimism chain {VelodromeExtensions.ChainId} is required.");
	}

	public async ValueTask<VelodromeToken> GetTokenAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		if (address.IsNativeToken())
			return new()
			{
				Address = address,
				Symbol = "ETH",
				Name = "Ether",
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
		if (symbol.IsEmpty())
			symbol = address[2..8].ToUpperInvariant();
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

	public async ValueTask<BigInteger> GetBalanceAsync(VelodromeToken token,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return (await SendAsync<VelodromeRpcAddressTagParameters,
				string>("eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<VelodromePool> GetPoolAsync(string poolAddress,
		CancellationToken cancellationToken)
	{
		poolAddress = poolAddress.NormalizeAddress();
		var code = await SendAsync<VelodromeRpcAddressTagParameters, string>(
			"eth_getCode", new()
			{
				Address = poolAddress,
				BlockTag = "latest",
			}, true, cancellationToken);
		if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
			code.EqualsIgnoreCase("0x0"))
			throw new InvalidDataException(
				$"Velodrome pool '{poolAddress}' is not a contract.");
		var token0Address = VelodromeExtensions.ReadAbiAddress(
			await CallContractAsync(poolAddress, "0x0dfe1681",
				cancellationToken), 0);
		var token1Address = VelodromeExtensions.ReadAbiAddress(
			await CallContractAsync(poolAddress, "0xd21220a7",
				cancellationToken), 0);
		var factory = VelodromeExtensions.ReadAbiAddress(
			await CallContractAsync(poolAddress, "0xc45a0155",
				cancellationToken), 0);
		VelodromePoolTypes poolType;
		string router;
		string quoter;
		var tickSpacing = 0;
		if (factory.IsClassicFactory())
		{
			var isStable = VelodromeExtensions.ReadAbiWord(
				await CallContractAsync(poolAddress,
					VelodromeExtensions.EncodeStaticCall("stable()"),
					cancellationToken), 0) != BigInteger.Zero;
			poolType = isStable
				? VelodromePoolTypes.Stable
				: VelodromePoolTypes.Volatile;
			router = VelodromeExtensions.ClassicRouterAddress
				.NormalizeAddress();
			quoter = null;
		}
		else if (factory.TryGetSlipstreamDeployment(out router, out quoter))
		{
			var spacing = VelodromeExtensions.ReadAbiWord(
				await CallContractAsync(poolAddress,
					VelodromeExtensions.EncodeStaticCall("tickSpacing()"),
					cancellationToken), 0);
			if (spacing <= 0 || spacing > 8_388_607)
				throw new InvalidDataException(
					$"Slipstream pool '{poolAddress}' returned invalid tick " +
					$"spacing '{spacing}'.");
			tickSpacing = (int)spacing;
			poolType = VelodromePoolTypes.Slipstream;
		}
		else
		{
			throw new InvalidDataException(
				$"Pool '{poolAddress}' was not created by an official " +
				"Velodrome factory.");
		}
		return new()
		{
			PoolId = poolAddress,
			PoolType = poolType,
			FactoryAddress = factory,
			RouterAddress = router,
			QuoterAddress = quoter,
			TickSpacing = tickSpacing,
			Token0 = await GetTokenAsync(token0Address, cancellationToken),
			Token1 = await GetTokenAsync(token1Address, cancellationToken),
		};
	}

	public async ValueTask<VelodromeQuote> GetQuoteAsync(
		VelodromeMarket market, VelodromeTradeTypes tradeType,
		BigInteger amount, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == VelodromeTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		if (market.PoolType != VelodromePoolTypes.Slipstream)
		{
			var input = isExactInput
				? amount
				: await GetClassicInputAsync(market, tokenIn, amount,
					cancellationToken);
			var output = isExactInput
				? await GetClassicOutputAsync(market, tokenIn, amount,
					cancellationToken)
				: amount;
			if (input <= 0 || output <= 0)
				throw new InvalidDataException(
					"Velodrome classic pool returned non-positive quote " +
					"amounts.");
			return new()
			{
				InputAmount = input,
				OutputAmount = output,
			};
		}
		else
		{
			var signature = isExactInput
				? "quoteExactInputSingle((address,address,uint256,int24,uint160))"
				: "quoteExactOutputSingle((address,address,uint256,int24,uint160))";
			var data = VelodromeExtensions.EncodeStaticCall(signature,
				VelodromeExtensions.AbiAddress(tokenIn),
				VelodromeExtensions.AbiAddress(tokenOut),
				VelodromeExtensions.AbiWord(amount),
				VelodromeExtensions.AbiWord(market.TickSpacing),
				VelodromeExtensions.AbiWord(BigInteger.Zero));
			var response = await CallContractAsync(market.QuoterAddress, data,
				cancellationToken);
			var quoted = VelodromeExtensions.ReadAbiWord(response, 0);
			if (quoted <= 0)
				throw new InvalidDataException(
					"Velodrome Slipstream returned a non-positive quote " +
					"amount.");
			return new()
			{
				InputAmount = isExactInput ? amount : quoted,
				OutputAmount = isExactInput ? quoted : amount,
				GasEstimate = VelodromeExtensions.ReadAbiWord(response, 3),
			};
		}
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(
		VelodromeToken token, string spender,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return BigInteger.Pow(2, 256) - 1;
		var data = VelodromeExtensions.EncodeStaticCall(
			"allowance(address,address)",
			VelodromeExtensions.AbiAddress(WalletAddress),
			VelodromeExtensions.AbiAddress(spender));
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public VelodromeTransaction CreateApprovalTransaction(
		VelodromeToken token, string spender, BigInteger amount)
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
			Data = VelodromeExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				VelodromeExtensions.AbiAddress(spender),
				VelodromeExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public VelodromeTransaction CreateSwapTransaction(
		VelodromeMarket market, VelodromeTradeTypes tradeType,
		BigInteger amount, VelodromeQuote quote, decimal slippageTolerance,
		DateTime deadline)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (amount <= 0 || quote.InputAmount <= 0 ||
			quote.OutputAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == VelodromeTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		if (tokenIn.IsNativeToken() || tokenOut.IsNativeToken())
			throw new NotSupportedException(
				"Use wrapped native tokens for direct Velodrome pools.");
		var slippageBps = new BigInteger(slippageTolerance * 100m);
		var minimumOutput = quote.OutputAmount *
			(10_000 - slippageBps) / 10_000;
		var maximumInput = (quote.InputAmount *
			(10_000 + slippageBps) + 9_999) / 10_000;
		if (minimumOutput <= 0 || maximumInput <= 0)
			throw new InvalidOperationException(
				"Slippage-adjusted Velodrome amount is non-positive.");
		var deadlineValue = new BigInteger(deadline.ToUnixSeconds());
		string router;
		string data;
		if (market.PoolType != VelodromePoolTypes.Slipstream)
		{
			router = market.RouterAddress;
			data = EncodeClassicSwap(
				isExactInput ? amount : maximumInput,
				isExactInput ? minimumOutput : amount,
				tokenIn, tokenOut,
				market.PoolType == VelodromePoolTypes.Stable,
				market.FactoryAddress, deadlineValue);
		}
		else
		{
			router = market.RouterAddress;
			var signature = isExactInput
				? "exactInputSingle((address,address,int24,address,uint256,uint256,uint256,uint160))"
				: "exactOutputSingle((address,address,int24,address,uint256,uint256,uint256,uint160))";
			data = VelodromeExtensions.EncodeStaticCall(signature,
				VelodromeExtensions.AbiAddress(tokenIn),
				VelodromeExtensions.AbiAddress(tokenOut),
				VelodromeExtensions.AbiWord(market.TickSpacing),
				VelodromeExtensions.AbiAddress(WalletAddress),
				VelodromeExtensions.AbiWord(deadlineValue),
				VelodromeExtensions.AbiWord(amount),
				VelodromeExtensions.AbiWord(isExactInput
					? minimumOutput
					: maximumInput),
				VelodromeExtensions.AbiWord(BigInteger.Zero));
		}
		return new()
		{
			To = router.NormalizeAddress(),
			Data = data,
			Value = BigInteger.Zero,
		};
	}

	public string GetSpender(VelodromeMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return market.RouterAddress.NormalizeAddress();
	}

	public async ValueTask<BigInteger> EstimateGasAsync(
		VelodromeTransaction transaction,
		CancellationToken cancellationToken)
	{
		var call = ToRpcCall(transaction);
		return (await SendAsync<VelodromeRpcCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = call }, true,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<string> SendTransactionAsync(
		VelodromeTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<VelodromeRpcAddressTagParameters,
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
					new BigInteger(VelodromeExtensions.ChainId), nonce,
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
					VelodromeRpcEmptyParameters, string>("eth_gasPrice",
					new(), true, cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(VelodromeExtensions.ChainId)
						.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey,
					tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<VelodromeRpcValueParameters, string>(
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

	public ValueTask<VelodromeRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<VelodromeRpcValueParameters, VelodromeRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = NormalizeHash(hash),
			}, true, cancellationToken);

	public async ValueTask<BigInteger> GetLatestBlockNumberAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<VelodromeRpcEmptyParameters, string>(
			"eth_blockNumber", new(), true, cancellationToken)).ParseInteger();

	public async ValueTask<VelodromeRpcBlock> GetBlockAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<VelodromeRpcTagBooleanParameters,
			VelodromeRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken);
		return block ?? throw new InvalidDataException(
			$"Optimism block '{blockNumber}' was not found.");
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		var block = await GetBlockAsync(blockNumber, cancellationToken);
		if (block.Timestamp.IsEmpty())
			throw new InvalidDataException(
				$"Optimism block '{blockNumber}' has no timestamp.");
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

	public ValueTask<VelodromeRpcLog[]> GetLogsAsync(VelodromeMarket market,
		BigInteger fromBlock, BigInteger toBlock,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (fromBlock < 0 || toBlock < fromBlock)
			throw new ArgumentOutOfRangeException(nameof(fromBlock));
		return SendAsync<VelodromeRpcLogsParameters, VelodromeRpcLog[]>(
			"eth_getLogs", new()
			{
				Filter = new()
				{
					FromBlock = fromBlock.ToRpcHex(),
					ToBlock = toBlock.ToRpcHex(),
					Address = market.PoolId,
					Topics = [market.PoolType.GetSwapTopic()],
				},
			}, true, cancellationToken);
	}

	public async ValueTask<VelodromeRpcReceipt> WaitForReceiptAsync(
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
				VelodromeRpcTagBooleanParameters, VelodromeRpcBlock>(
				"eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<
				VelodromeRpcEmptyParameters, string>(
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
		=> await SendAsync<VelodromeRpcCallParameters, string>("eth_call",
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
		where TParameters : VelodromeRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new VelodromeRpcRequest<TParameters>
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
			VelodromeRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					VelodromeRpcResponse<TResult>>(body, _jsonSettings);
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

	private async ValueTask<BigInteger> GetClassicOutputAsync(
		VelodromeMarket market, string tokenIn, BigInteger amountIn,
		CancellationToken cancellationToken)
	{
		var data = VelodromeExtensions.EncodeStaticCall(
			"getAmountOut(uint256,address)",
			VelodromeExtensions.AbiWord(amountIn),
			VelodromeExtensions.AbiAddress(tokenIn));
		return VelodromeExtensions.ReadAbiWord(await CallContractAsync(
			market.PoolId, data, cancellationToken), 0);
	}

	private async ValueTask<BigInteger> GetClassicInputAsync(
		VelodromeMarket market, string tokenIn, BigInteger amountOut,
		CancellationToken cancellationToken)
	{
		var tokenOut = tokenIn.EqualsIgnoreCase(market.Token0.Address)
			? market.Token1.Address
			: market.Token0.Address;
		var reverse = await GetClassicOutputAsync(market, tokenOut, amountOut,
			cancellationToken);
		var low = BigInteger.Zero;
		var high = BigInteger.Max(reverse, BigInteger.One);
		for (var index = 0; index < 64; index++)
		{
			var output = await GetClassicOutputAsync(market, tokenIn, high,
				cancellationToken);
			if (output >= amountOut)
				break;
			low = high;
			high *= 2;
			if (index == 63)
				throw new InvalidDataException(
					"Velodrome classic exact-output quote did not converge.");
		}
		if (await GetClassicOutputAsync(market, tokenIn, high,
			cancellationToken) < amountOut)
			throw new InvalidDataException(
				"Velodrome classic pool has insufficient quote liquidity.");
		for (var index = 0; index < 64 && high - low > 1; index++)
		{
			var middle = (low + high) / 2;
			if (await GetClassicOutputAsync(market, tokenIn, middle,
				cancellationToken) >= amountOut)
				high = middle;
			else
				low = middle;
		}
		return high;
	}

	private string EncodeClassicSwap(BigInteger amountIn,
		BigInteger amountOutMinimum, string tokenIn, string tokenOut,
		bool isStable, string factory, BigInteger deadline)
		=> "0x" + VelodromeExtensions.AbiSelector(
			"swapExactTokensForTokens(uint256,uint256,(address,address,bool,address)[],address,uint256)") +
			VelodromeExtensions.AbiWord(amountIn) +
			VelodromeExtensions.AbiWord(amountOutMinimum) +
			VelodromeExtensions.AbiWord(160) +
			VelodromeExtensions.AbiAddress(WalletAddress) +
			VelodromeExtensions.AbiWord(deadline) +
			VelodromeExtensions.AbiWord(1) +
			VelodromeExtensions.AbiAddress(tokenIn) +
			VelodromeExtensions.AbiAddress(tokenOut) +
			VelodromeExtensions.AbiWord(isStable
				? BigInteger.One
				: BigInteger.Zero) +
			VelodromeExtensions.AbiAddress(factory);

	private void ValidateTransaction(VelodromeTransaction transaction)
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

	private VelodromeRpcCall ToRpcCall(VelodromeTransaction transaction)
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
				"An EVM private key is required for Velodrome transactions.");
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
