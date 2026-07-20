namespace StockSharp.Curve.Native;

sealed class CurveRpcClient : BaseLogReceiver
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

	public CurveRpcClient(string endpoint, string walletAddress,
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
				? CurveExtensions.NativeTokenAddress
				: walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Curve-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Curve_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsSigningAvailable => _privateKey is not null;

	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		CurveExtensions.NativeTokenAddress);

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
		var chainId = (await SendAsync<CurveRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(CurveExtensions.ChainId))
			throw new InvalidOperationException(
				$"JSON-RPC is connected to chain {chainId}, but " +
				$"Ethereum chain {CurveExtensions.ChainId} is required.");
	}

	public async ValueTask VerifyContractAsync(string address, string name,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		var code = await SendAsync<CurveRpcAddressTagParameters, string>(
			"eth_getCode", new()
			{
				Address = address,
				BlockTag = "latest",
			}, true, cancellationToken);
		if (code.IsEmpty() || code.EqualsIgnoreCase("0x") ||
			code.EqualsIgnoreCase("0x0"))
			throw new InvalidDataException(
				$"{name.ThrowIfEmpty(nameof(name))} '{address}' is not a " +
				"contract on Ethereum.");
	}

	public async ValueTask<CurveToken> GetTokenAsync(CurveApiCoin source,
		int poolIndex, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (poolIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(poolIndex));
		var address = source.Address.NormalizeAddress();
		if (address.IsNativeToken())
			throw new NotSupportedException(
				"Native-asset Curve routes are not supported; configure an " +
				"ERC-20 or wrapped-token pool.");
		await VerifyContractAsync(address, "Curve coin", cancellationToken);
		var decimalsValue = await CallContractAsync(address, "0x313ce567",
			cancellationToken);
		var decimalsInteger = decimalsValue.ParseInteger();
		if (decimalsInteger < 0 || decimalsInteger > 255)
			throw new InvalidDataException(
				$"Token '{address}' returned invalid decimals value.");
		if (!int.TryParse(source.Decimals, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var apiDecimals) ||
			apiDecimals != (int)decimalsInteger)
			throw new InvalidDataException(
				$"Curve API decimals for coin '{address}' do not match the " +
				"on-chain contract.");
		var symbol = source.Symbol?.Trim();
		var name = source.Name?.Trim();
		if (symbol.IsEmpty())
			symbol = await TryReadTextAsync(address, "0x95d89b41",
				cancellationToken);
		if (name.IsEmpty())
			name = await TryReadTextAsync(address, "0x06fdde03",
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
			PoolIndex = poolIndex,
		};
	}

	public static CurveToken CreateNativeToken()
		=> new()
		{
			Address = CurveExtensions.NativeTokenAddress,
			Symbol = "ETH",
			Name = "Ether",
			Decimals = 18,
			PoolIndex = -1,
		};

	public async ValueTask<string> GetPoolCoinAddressAsync(string poolId,
		int poolIndex, CancellationToken cancellationToken)
	{
		if (poolIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(poolIndex));
		var result = await CallContractAsync(poolId,
			CurveExtensions.EncodeStaticCall("coins(uint256)",
				CurveExtensions.AbiWord(poolIndex)), cancellationToken);
		var address = CurveExtensions.ReadAbiAddress(result, 0);
		if (address.IsZeroAddress())
			throw new InvalidDataException(
				$"Curve pool '{poolId}' returned an empty coin at index " +
				$"{poolIndex}.");
		return address;
	}

	public async ValueTask<BigInteger> GetBalanceAsync(CurveToken token,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return (await SendAsync<CurveRpcAddressTagParameters,
				string>("eth_getBalance", new()
				{
					Address = WalletAddress,
					BlockTag = "latest",
				}, true, cancellationToken)).ParseInteger();
		var data = "0x70a08231" + WalletAddress[2..].PadLeft(64, '0');
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<CurveQuote> GetQuoteAsync(
		CurveMarket market, CurveTradeTypes tradeType,
		BigInteger amount, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == CurveTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var input = isExactInput
			? amount
			: await GetRequiredInputAsync(market, tokenIn, amount,
				cancellationToken);
		var output = isExactInput
			? await GetRouterOutputAsync(market, tokenIn, amount,
				cancellationToken)
			: amount;
		if (input <= 0 || output <= 0)
			throw new InvalidDataException(
				"Curve Router NG returned non-positive quote amounts.");
		return new()
		{
			InputAmount = input,
			OutputAmount = output,
		};
	}

	public async ValueTask<BigInteger> GetAllowanceAsync(
		CurveToken token, string spender,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (token.Address.IsNativeToken())
			return BigInteger.Pow(2, 256) - 1;
		var data = CurveExtensions.EncodeStaticCall(
			"allowance(address,address)",
			CurveExtensions.AbiAddress(WalletAddress),
			CurveExtensions.AbiAddress(spender));
		return (await CallContractAsync(token.Address, data,
			cancellationToken)).ParseInteger();
	}

	public CurveTransaction CreateApprovalTransaction(
		CurveToken token, string spender, BigInteger amount)
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
			Data = CurveExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				CurveExtensions.AbiAddress(spender),
				CurveExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public CurveTransaction CreateSwapTransaction(
		CurveMarket market, CurveTradeTypes tradeType,
		BigInteger amount, CurveQuote quote, decimal slippageTolerance,
		DateTime deadline)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (amount <= 0 || quote.InputAmount <= 0 ||
			quote.OutputAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var isExactInput = tradeType == CurveTradeTypes.ExactInput;
		var tokenIn = isExactInput
			? market.BaseToken.Address
			: market.QuoteToken.Address;
		var tokenOut = isExactInput
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		if (tokenIn.IsNativeToken() || tokenOut.IsNativeToken())
			throw new NotSupportedException(
				"Use wrapped native tokens for direct Curve pools.");
		var slippageBps = new BigInteger(slippageTolerance * 100m);
		var minimumOutput = quote.OutputAmount *
			(10_000 - slippageBps) / 10_000;
		if (minimumOutput <= 0)
			throw new InvalidOperationException(
				"Slippage-adjusted Curve amount is non-positive.");
		_ = deadline;
		var data = EncodeRouterSwap(market, tokenIn, tokenOut,
			quote.InputAmount, minimumOutput);
		return new()
		{
			To = market.RouterAddress.NormalizeAddress(),
			Data = data,
			Value = BigInteger.Zero,
		};
	}

	public string GetSpender(CurveMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return market.RouterAddress.NormalizeAddress();
	}

	public async ValueTask<BigInteger> EstimateGasAsync(
		CurveTransaction transaction,
		CancellationToken cancellationToken)
	{
		var call = ToRpcCall(transaction);
		return (await SendAsync<CurveRpcCallOnlyParameters, string>(
			"eth_estimateGas", new() { Call = call }, true,
			cancellationToken)).ParseInteger();
	}

	public async ValueTask<string> SendTransactionAsync(
		CurveTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<CurveRpcAddressTagParameters,
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
					new BigInteger(CurveExtensions.ChainId), nonce,
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
					CurveRpcEmptyParameters, string>("eth_gasPrice",
					new(), true, cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(CurveExtensions.ChainId)
						.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey,
					tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<CurveRpcValueParameters, string>(
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

	public ValueTask<CurveRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<CurveRpcValueParameters, CurveRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<CurveRpcBlock> GetBlockAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<CurveRpcTagBooleanParameters,
			CurveRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken);
		return block ?? throw new InvalidDataException(
			$"Ethereum block '{blockNumber}' was not found.");
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(
		BigInteger blockNumber, CancellationToken cancellationToken)
	{
		var block = await GetBlockAsync(blockNumber, cancellationToken);
		if (block.Timestamp.IsEmpty())
			throw new InvalidDataException(
				$"Ethereum block '{blockNumber}' has no timestamp.");
		return block.Timestamp.ParseInteger().ToUtcTime();
	}

	public async ValueTask<CurveRpcReceipt> WaitForReceiptAsync(
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
				CurveRpcTagBooleanParameters, CurveRpcBlock>(
				"eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<
				CurveRpcEmptyParameters, string>(
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
		=> await SendAsync<CurveRpcCallParameters, string>("eth_call",
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
		where TParameters : CurveRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new CurveRpcRequest<TParameters>
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
			CurveRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					CurveRpcResponse<TResult>>(body, _jsonSettings);
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

	private async ValueTask<BigInteger> GetRouterOutputAsync(
		CurveMarket market, string tokenIn, BigInteger amountIn,
		CancellationToken cancellationToken)
	{
		var tokenOut = tokenIn.EqualsIgnoreCase(market.BaseToken.Address)
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		var data = EncodeRouterQuote(market, tokenIn, tokenOut, amountIn);
		return CurveExtensions.ReadAbiWord(await CallContractAsync(
			market.RouterAddress, data, cancellationToken), 0);
	}

	private async ValueTask<BigInteger> GetRequiredInputAsync(
		CurveMarket market, string tokenIn, BigInteger amountOut,
		CancellationToken cancellationToken)
	{
		var tokenOut = tokenIn.EqualsIgnoreCase(market.BaseToken.Address)
			? market.QuoteToken.Address
			: market.BaseToken.Address;
		var reverse = await GetRouterOutputAsync(market, tokenOut, amountOut,
			cancellationToken);
		var input = BigInteger.Max(reverse, BigInteger.One);
		for (var index = 0; index < 12; index++)
		{
			var output = await GetRouterOutputAsync(market, tokenIn, input,
				cancellationToken);
			if (output >= amountOut)
				return input;
			if (output <= 0)
				input *= 2;
			else
			{
				var adjusted = (input * amountOut + output - 1) / output;
				input = adjusted > input ? adjusted : input + 1;
			}
		}
		throw new InvalidDataException(
			"Curve exact-output quote did not converge.");
	}

	private static string EncodeRouterQuote(CurveMarket market,
		string tokenIn, string tokenOut, BigInteger amountIn)
		=> "0x" + CurveExtensions.AbiSelector(
			"get_dy(address[11],uint256[5][5],uint256,address[5])") +
			EncodeRoute(market, tokenIn, tokenOut) +
			EncodeSwapParameters(market, tokenIn) +
			CurveExtensions.AbiWord(amountIn) +
			EncodeZeroAddresses(5);

	private string EncodeRouterSwap(CurveMarket market, string tokenIn,
		string tokenOut, BigInteger amountIn, BigInteger amountOutMinimum)
		=> "0x" + CurveExtensions.AbiSelector(
			"exchange(address[11],uint256[5][5],uint256,uint256,address[5],address)") +
			EncodeRoute(market, tokenIn, tokenOut) +
			EncodeSwapParameters(market, tokenIn) +
			CurveExtensions.AbiWord(amountIn) +
			CurveExtensions.AbiWord(amountOutMinimum) +
			EncodeZeroAddresses(5) +
			CurveExtensions.AbiAddress(WalletAddress);

	private static string EncodeRoute(CurveMarket market, string tokenIn,
		string tokenOut)
		=> CurveExtensions.AbiAddress(tokenIn) +
			CurveExtensions.AbiAddress(market.PoolId) +
			CurveExtensions.AbiAddress(tokenOut) + EncodeZeroAddresses(8);

	private static string EncodeSwapParameters(CurveMarket market,
		string tokenIn)
	{
		var inputIndex = tokenIn.EqualsIgnoreCase(market.BaseToken.Address)
			? market.BaseToken.PoolIndex
			: market.QuoteToken.PoolIndex;
		var outputIndex = tokenIn.EqualsIgnoreCase(market.BaseToken.Address)
			? market.QuoteToken.PoolIndex
			: market.BaseToken.PoolIndex;
		return CurveExtensions.AbiWord(inputIndex) +
			CurveExtensions.AbiWord(outputIndex) +
			CurveExtensions.AbiWord(BigInteger.One) +
			CurveExtensions.AbiWord(market.PoolType.ToRouterPoolType()) +
			CurveExtensions.AbiWord(market.PoolCoinCount) +
			string.Concat(Enumerable.Repeat(
				CurveExtensions.AbiWord(BigInteger.Zero), 20));
	}

	private static string EncodeZeroAddresses(int count)
		=> string.Concat(Enumerable.Repeat(
			CurveExtensions.AbiAddress(CurveExtensions.NativeTokenAddress),
			count));

	private void ValidateTransaction(CurveTransaction transaction)
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

	private CurveRpcCall ToRpcCall(CurveTransaction transaction)
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
				"An EVM private key is required for Curve transactions.");
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
