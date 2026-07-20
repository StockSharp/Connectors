namespace StockSharp.QuickSwap.Native;

sealed class QuickSwapRpcClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly QuickSwapChains _chain;
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

	public QuickSwapRpcClient(string endpoint, QuickSwapChains chain,
		string walletAddress, SecureString privateKey)
	{
		if (!Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported QuickSwap chain.");
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
			"StockSharp-QuickSwap-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "QuickSwap_JSON-RPC";

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
		var chainId = (await SendAsync<QuickSwapRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger((int)_chain))
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but " +
				$"{(int)_chain} ({_chain}) is configured.");
	}

	public async ValueTask<QuickSwapToken> GetTokenAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		if (address.IsNativeToken())
			return new()
			{
				Address = address,
				Symbol = _chain.NativeSymbol(),
				Name = _chain.NativeSymbol(),
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

	public async ValueTask<BigInteger> GetBalanceAsync(QuickSwapToken token,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return (await SendAsync<QuickSwapRpcAddressTagParameters,
				string>("eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<string> GetPoolAddressAsync(
		QuickSwapPoolVersions poolVersion, string tokenA, string tokenB,
		CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(poolVersion))
			throw new ArgumentOutOfRangeException(nameof(poolVersion),
				poolVersion, "Unsupported QuickSwap pool version.");
		tokenA = tokenA.NormalizeAddress();
		tokenB = tokenB.NormalizeAddress();
		if (tokenA.IsNativeToken() || tokenB.IsNativeToken())
			throw new NotSupportedException(
				"Direct QuickSwap pools use wrapped native tokens.");
		string factory;
		string data;
		if (poolVersion == QuickSwapPoolVersions.V2)
		{
			factory = _chain.GetV2Factory();
			data = QuickSwapExtensions.EncodeStaticCall(
				"getPair(address,address)",
				QuickSwapExtensions.AbiAddress(tokenA),
				QuickSwapExtensions.AbiAddress(tokenB));
		}
		else
		{
			factory = _chain.GetV3Factory();
			data = QuickSwapExtensions.EncodeStaticCall(
				"poolByPair(address,address)",
				QuickSwapExtensions.AbiAddress(tokenA),
				QuickSwapExtensions.AbiAddress(tokenB));
		}
		var response = await CallContractAsync(factory, data,
			cancellationToken);
		var pool = QuickSwapExtensions.ReadAbiAddress(response, 0);
		if (pool.EqualsIgnoreCase(
			QuickSwapExtensions.NativeTokenAddress))
			throw new InvalidOperationException(
				$"No QuickSwap {poolVersion} pool exists for " +
				$"{tokenA}/{tokenB}.");
		var code = await SendAsync<QuickSwapRpcAddressTagParameters,
			string>("eth_getCode", new()
			{
				Address = pool,
				BlockTag = "latest",
			}, true, cancellationToken);
		if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
			code.EqualsIgnoreCase("0x0"))
			throw new InvalidDataException(
				$"QuickSwap factory returned non-contract address '{pool}'.");
		return pool;
	}

	public async ValueTask<QuickSwapQuote> GetQuoteAsync(
		QuickSwapMarket market, QuickSwapTradeTypes tradeType,
		BigInteger amount, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (!Enum.IsDefined(market.PoolVersion))
			throw new ArgumentOutOfRangeException(nameof(market.PoolVersion),
				market.PoolVersion, "Unsupported QuickSwap pool version.");
		if (!Enum.IsDefined(tradeType))
			throw new ArgumentOutOfRangeException(nameof(tradeType),
				tradeType, "Unsupported QuickSwap trade type.");
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == QuickSwapTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		if (market.PoolVersion == QuickSwapPoolVersions.V2)
		{
			var signature = isExactInput
				? "getAmountsOut(uint256,address[])"
				: "getAmountsIn(uint256,address[])";
			var data = EncodeV2Quote(signature, amount, tokenIn, tokenOut);
			var response = await CallContractAsync(_chain.GetV2Router(), data,
				cancellationToken);
			ValidateV2Amounts(response);
			var input = QuickSwapExtensions.ReadAbiWord(response, 2);
			var output = QuickSwapExtensions.ReadAbiWord(response, 3);
			if (input <= 0 || output <= 0)
				throw new InvalidDataException(
					"QuickSwap v2 returned non-positive quote amounts.");
			return new()
			{
				InputAmount = input,
				OutputAmount = output,
			};
		}
		else
		{
			var signature = isExactInput
				? "quoteExactInputSingle(address,address,uint256,uint160)"
				: "quoteExactOutputSingle(address,address,uint256,uint160)";
			var data = QuickSwapExtensions.EncodeStaticCall(signature,
				QuickSwapExtensions.AbiAddress(tokenIn),
				QuickSwapExtensions.AbiAddress(tokenOut),
				QuickSwapExtensions.AbiWord(amount),
				QuickSwapExtensions.AbiWord(BigInteger.Zero));
			var response = await CallContractAsync(_chain.GetV3Quoter(), data,
				cancellationToken);
			var quoted = QuickSwapExtensions.ReadAbiWord(response, 0);
			if (quoted <= 0)
				throw new InvalidDataException(
					"QuickSwap v3 returned a non-positive quote amount.");
			var fee = QuickSwapExtensions.ReadAbiWord(response, 1);
			if (fee < 0 || fee > ushort.MaxValue)
				throw new InvalidDataException(
					"QuickSwap v3 returned an invalid dynamic fee.");
			return new()
			{
				InputAmount = isExactInput ? amount : quoted,
				OutputAmount = isExactInput ? quoted : amount,
			};
		}
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(
		QuickSwapToken token, string spender,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return BigInteger.Pow(2, 256) - 1;
		var data = QuickSwapExtensions.EncodeStaticCall(
			"allowance(address,address)",
			QuickSwapExtensions.AbiAddress(WalletAddress),
			QuickSwapExtensions.AbiAddress(spender));
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public QuickSwapTransaction CreateApprovalTransaction(
		QuickSwapToken token, string spender, BigInteger amount)
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
			Data = QuickSwapExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				QuickSwapExtensions.AbiAddress(spender),
				QuickSwapExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public QuickSwapTransaction CreateSwapTransaction(
		QuickSwapMarket market, QuickSwapTradeTypes tradeType,
		BigInteger amount, QuickSwapQuote quote, decimal slippageTolerance,
		DateTime deadline)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (!Enum.IsDefined(market.PoolVersion))
			throw new ArgumentOutOfRangeException(nameof(market.PoolVersion),
				market.PoolVersion, "Unsupported QuickSwap pool version.");
		if (!Enum.IsDefined(tradeType))
			throw new ArgumentOutOfRangeException(nameof(tradeType),
				tradeType, "Unsupported QuickSwap trade type.");
		if (amount <= 0 || quote.InputAmount <= 0 ||
			quote.OutputAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == QuickSwapTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		if (tokenIn.IsNativeToken() || tokenOut.IsNativeToken())
			throw new NotSupportedException(
				"Use wrapped native tokens for direct QuickSwap pools.");
		var slippageBps = new BigInteger(slippageTolerance * 100m);
		var minimumOutput = quote.OutputAmount *
			(10_000 - slippageBps) / 10_000;
		var maximumInput = (quote.InputAmount *
			(10_000 + slippageBps) + 9_999) / 10_000;
		if (minimumOutput <= 0 || maximumInput <= 0)
			throw new InvalidOperationException(
				"Slippage-adjusted QuickSwap amount is non-positive.");
		var deadlineValue = new BigInteger(deadline.ToUnixSeconds());
		string router;
		string data;
		if (market.PoolVersion == QuickSwapPoolVersions.V2)
		{
			router = _chain.GetV2Router();
			data = isExactInput
				? EncodeV2Swap(
					"swapExactTokensForTokens(uint256,uint256,address[],address,uint256)",
					amount, minimumOutput, tokenIn, tokenOut, deadlineValue)
				: EncodeV2Swap(
					"swapTokensForExactTokens(uint256,uint256,address[],address,uint256)",
					amount, maximumInput, tokenIn, tokenOut, deadlineValue);
		}
		else
		{
			router = _chain.GetV3Router();
			var signature = isExactInput
				? "exactInputSingle((address,address,address,uint256,uint256,uint256,uint160))"
				: "exactOutputSingle((address,address,address,uint256,uint256,uint256,uint160))";
			data = QuickSwapExtensions.EncodeStaticCall(signature,
				QuickSwapExtensions.AbiAddress(tokenIn),
				QuickSwapExtensions.AbiAddress(tokenOut),
				QuickSwapExtensions.AbiAddress(WalletAddress),
				QuickSwapExtensions.AbiWord(deadlineValue),
				QuickSwapExtensions.AbiWord(amount),
				QuickSwapExtensions.AbiWord(isExactInput
					? minimumOutput
					: maximumInput),
				QuickSwapExtensions.AbiWord(BigInteger.Zero));
		}
		return new()
		{
			To = router.NormalizeAddress(),
			Data = data,
			Value = BigInteger.Zero,
		};
	}

	public string GetSpender(QuickSwapPoolVersions poolVersion)
		=> poolVersion switch
		{
			QuickSwapPoolVersions.V2 =>
				_chain.GetV2Router().NormalizeAddress(),
			QuickSwapPoolVersions.V3 =>
				_chain.GetV3Router().NormalizeAddress(),
			_ => throw new ArgumentOutOfRangeException(nameof(poolVersion),
				poolVersion, "Unsupported QuickSwap pool version."),
		};

	public async ValueTask<BigInteger> EstimateGasAsync(
		QuickSwapTransaction transaction,
		CancellationToken cancellationToken)
	{
		var call = ToRpcCall(transaction);
		return (await SendAsync<QuickSwapRpcCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = call }, true,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<string> SendTransactionAsync(
		QuickSwapTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<QuickSwapRpcAddressTagParameters,
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
					new BigInteger((int)_chain), nonce, eip1559.PriorityFee,
					eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<
					QuickSwapRpcEmptyParameters, string>("eth_gasPrice",
					new(), true, cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger((int)_chain).ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey,
					tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<QuickSwapRpcValueParameters, string>(
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

	public async ValueTask<QuickSwapRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
	{
		hash = NormalizeHash(hash);
		var receipt = await SendAsync<QuickSwapRpcValueParameters,
			QuickSwapRpcReceipt>("eth_getTransactionReceipt", new()
			{
				Value = hash,
			}, true, cancellationToken);
		if (receipt is null)
			return null;
		if (!NormalizeHash(receipt.TransactionHash).EqualsIgnoreCase(hash))
			throw new InvalidDataException(
				"JSON-RPC returned a receipt for a different transaction.");
		if (receipt.BlockNumber.IsEmpty() ||
			receipt.BlockNumber.ParseInteger() < 0)
			throw new InvalidDataException(
				$"Transaction '{hash}' has an invalid receipt block number.");
		return receipt;
	}

	public async ValueTask<QuickSwapRpcReceipt> WaitForReceiptAsync(
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
				QuickSwapRpcTagBooleanParameters, QuickSwapRpcBlock>(
				"eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<
				QuickSwapRpcEmptyParameters, string>(
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
		=> await SendAsync<QuickSwapRpcCallParameters, string>("eth_call",
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
		where TParameters : QuickSwapRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new QuickSwapRpcRequest<TParameters>
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
			QuickSwapRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					QuickSwapRpcResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"JSON-RPC returned an unexpected response shape.",
					error);
			}
			if (rpc is null || rpc.Id != requestId ||
				!rpc.JsonRpc.EqualsIgnoreCase("2.0"))
				throw new InvalidDataException(
					"JSON-RPC returned an invalid response envelope.");
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

	private static string EncodeV2Quote(string signature, BigInteger amount,
		string tokenIn, string tokenOut)
		=> "0x" + QuickSwapExtensions.AbiSelector(signature) +
			QuickSwapExtensions.AbiWord(amount) +
			QuickSwapExtensions.AbiWord(64) +
			QuickSwapExtensions.AbiWord(2) +
			QuickSwapExtensions.AbiAddress(tokenIn) +
			QuickSwapExtensions.AbiAddress(tokenOut);

	private string EncodeV2Swap(string signature, BigInteger firstAmount,
		BigInteger secondAmount, string tokenIn, string tokenOut,
		BigInteger deadline)
		=> "0x" + QuickSwapExtensions.AbiSelector(signature) +
			QuickSwapExtensions.AbiWord(firstAmount) +
			QuickSwapExtensions.AbiWord(secondAmount) +
			QuickSwapExtensions.AbiWord(160) +
			QuickSwapExtensions.AbiAddress(WalletAddress) +
			QuickSwapExtensions.AbiWord(deadline) +
			QuickSwapExtensions.AbiWord(2) +
			QuickSwapExtensions.AbiAddress(tokenIn) +
			QuickSwapExtensions.AbiAddress(tokenOut);

	private static void ValidateV2Amounts(string value)
	{
		if (QuickSwapExtensions.ReadAbiWord(value, 0) != 32 ||
			QuickSwapExtensions.ReadAbiWord(value, 1) != 2)
			throw new InvalidDataException(
				"QuickSwap v2 returned an invalid amount array.");
	}

	private void ValidateTransaction(QuickSwapTransaction transaction)
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

	private QuickSwapRpcCall ToRpcCall(QuickSwapTransaction transaction)
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
				"An EVM private key is required for QuickSwap transactions.");
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
