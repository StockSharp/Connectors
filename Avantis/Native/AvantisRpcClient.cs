namespace StockSharp.Avantis.Native;

sealed class AvantisRpcClient : BaseLogReceiver
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

	public AvantisRpcClient(string endpoint, string walletAddress,
		SecureString privateKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Base JSON-RPC endpoint must use HTTP or HTTPS.",
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
						"The configured Avantis wallet address does not match " +
						"the private key.", nameof(walletAddress));
				WalletAddress = derived;
			}
			catch (ArgumentException)
			{
				throw;
			}
			catch (Exception error)
			{
				throw new ArgumentException("Invalid EVM private key.",
					nameof(privateKey), error);
			}
		}
		else if (!walletAddress.IsEmpty())
			WalletAddress = walletAddress.NormalizeAddress();

		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Avantis-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Avantis_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsWalletConfigured => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public async ValueTask VerifyChainAsync(
		CancellationToken cancellationToken)
	{
		var chainId = (await SendAsync<AvantisRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(AvantisExtensions.ChainId))
			throw new InvalidOperationException(
				"JSON-RPC is connected to chain " + chainId +
				", but Base chain 8453 is required.");
	}

	public async ValueTask<BigInteger> GetEthBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		return (await SendAsync<AvantisRpcAddressTagParameters, string>(
			"eth_getBalance", new()
			{
				Address = WalletAddress,
				BlockTag = "latest",
			}, true, cancellationToken)).ParseInteger();
	}

	public async ValueTask<BigInteger> GetUsdcBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var data = AvantisExtensions.EncodeStaticCall("balanceOf(address)",
			AvantisExtensions.AbiAddress(WalletAddress));
		return AvantisExtensions.ReadAbiWord(await CallContractAsync(
			AvantisExtensions.UsdcAddress, data, cancellationToken), 0);
	}

	public async ValueTask<BigInteger> GetUsdcAllowanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var data = AvantisExtensions.EncodeStaticCall(
			"allowance(address,address)",
			AvantisExtensions.AbiAddress(WalletAddress),
			AvantisExtensions.AbiAddress(
				AvantisExtensions.TradingStorageAddress));
		return AvantisExtensions.ReadAbiWord(await CallContractAsync(
			AvantisExtensions.UsdcAddress, data, cancellationToken), 0);
	}

	public AvantisTransaction CreateApprovalTransaction(BigInteger amount)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return new()
		{
			To = AvantisExtensions.UsdcAddress,
			Data = AvantisExtensions.EncodeStaticCall("approve(address,uint256)",
				AvantisExtensions.AbiAddress(
					AvantisExtensions.TradingStorageAddress),
				AvantisExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public AvantisTransaction CreateOpenTradeTransaction(int pairIndex,
		Sides side, BigInteger collateral, BigInteger openPrice,
		BigInteger leverage, BigInteger takeProfit, BigInteger stopLoss,
		AvantisOpenOrderTypes orderType, BigInteger slippage,
		BigInteger executionFee)
	{
		EnsureWalletConfigured();
		if (pairIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		if (collateral <= 0 || openPrice <= 0 || leverage <= 0 ||
			takeProfit < 0 || stopLoss < 0 || slippage < 0 ||
			executionFee < 0)
			throw new ArgumentOutOfRangeException(nameof(collateral),
				"Avantis trade parameters must be positive.");
		var data = "0x" + AvantisExtensions.AbiSelector(
			"openTrade((address,uint256,uint256,uint256,uint256,uint256,bool,uint256,uint256,uint256,uint256),uint8,uint256)") +
			AvantisExtensions.AbiAddress(WalletAddress) +
			AvantisExtensions.AbiWord(pairIndex) +
			AvantisExtensions.AbiWord(BigInteger.Zero) +
			AvantisExtensions.AbiWord(BigInteger.Zero) +
			AvantisExtensions.AbiWord(collateral) +
			AvantisExtensions.AbiWord(openPrice) +
			AvantisExtensions.AbiWord(side == Sides.Buy
				? BigInteger.One
				: BigInteger.Zero) +
			AvantisExtensions.AbiWord(leverage) +
			AvantisExtensions.AbiWord(takeProfit) +
			AvantisExtensions.AbiWord(stopLoss) +
			AvantisExtensions.AbiWord(BigInteger.Zero) +
			AvantisExtensions.AbiWord((int)orderType) +
			AvantisExtensions.AbiWord(slippage);
		return new()
		{
			To = AvantisExtensions.TradingAddress,
			Data = data,
			Value = executionFee,
		};
	}

	public AvantisTransaction CreateCloseTradeTransaction(int pairIndex,
		int tradeIndex, BigInteger collateral, BigInteger executionFee)
	{
		if (pairIndex < 0 || tradeIndex < 0 || collateral <= 0 ||
			executionFee < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = AvantisExtensions.TradingAddress,
			Data = AvantisExtensions.EncodeStaticCall(
				"closeTradeMarket(uint256,uint256,uint256)",
				AvantisExtensions.AbiWord(pairIndex),
				AvantisExtensions.AbiWord(tradeIndex),
				AvantisExtensions.AbiWord(collateral)),
			Value = executionFee,
		};
	}

	public AvantisTransaction CreateCancelLimitTransaction(int pairIndex,
		int tradeIndex)
	{
		if (pairIndex < 0 || tradeIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = AvantisExtensions.TradingAddress,
			Data = AvantisExtensions.EncodeStaticCall(
				"cancelOpenLimitOrder(uint256,uint256)",
				AvantisExtensions.AbiWord(pairIndex),
				AvantisExtensions.AbiWord(tradeIndex)),
			Value = BigInteger.Zero,
		};
	}

	public AvantisTransaction CreateUpdateLimitTransaction(int pairIndex,
		int tradeIndex, BigInteger price, BigInteger slippage,
		BigInteger takeProfit, BigInteger stopLoss)
	{
		if (pairIndex < 0 || tradeIndex < 0 || price <= 0 ||
			slippage < 0 || takeProfit < 0 || stopLoss < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = AvantisExtensions.TradingAddress,
			Data = AvantisExtensions.EncodeStaticCall(
				"updateOpenLimitOrder(uint256,uint256,uint256,uint256,uint256,uint256)",
				AvantisExtensions.AbiWord(pairIndex),
				AvantisExtensions.AbiWord(tradeIndex),
				AvantisExtensions.AbiWord(price),
				AvantisExtensions.AbiWord(slippage),
				AvantisExtensions.AbiWord(takeProfit),
				AvantisExtensions.AbiWord(stopLoss)),
			Value = BigInteger.Zero,
		};
	}

	public async ValueTask<AvantisRpcReceipt> SendAndWaitAsync(
		AvantisTransaction transaction, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var hash = await SendTransactionAsync(transaction, cancellationToken);
		var receipt = await WaitForReceiptAsync(hash, timeout,
			cancellationToken);
		EnsureSuccessful(receipt);
		return receipt;
	}

	public async ValueTask<string> SendTransactionAsync(
		AvantisTransaction transaction, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<AvantisRpcAddressTagParameters,
				string>("eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger();
			var estimated = await EstimateGasAsync(transaction,
				cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"The Avantis transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(
					new BigInteger(AvantisExtensions.ChainId), nonce,
					eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<AvantisRpcEmptyParameters,
					string>("eth_gasPrice", new(), true,
					cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(AvantisExtensions.ChainId)
						.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<AvantisRpcValueParameters, string>(
				"eth_sendRawTransaction", new()
				{
					Value = encoded.ToHex(true),
				}, false, cancellationToken);
			return hash.NormalizeHash();
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	public ValueTask<AvantisRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<AvantisRpcValueParameters, AvantisRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<AvantisRpcReceipt> WaitForReceiptAsync(string hash,
		TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeout));
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
			"Avantis transaction '" + hash + "' was not mined within " +
			timeout + ".");
	}

	public async ValueTask<DateTime> GetReceiptTimeAsync(
		AvantisRpcReceipt receipt, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		return await GetBlockTimeAsync(receipt.BlockNumber.ParseInteger(),
			cancellationToken);
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(BigInteger blockNumber,
		CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<AvantisRpcTagBooleanParameters,
			AvantisRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken) ?? throw new InvalidDataException(
				"Base returned no requested block.");
		var seconds = block.Timestamp.ParseInteger();
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				"Base returned an invalid block timestamp.");
		return ((long)seconds).FromUnix();
	}

	public AvantisLimitEvent TryGetLimitEvent(AvantisRpcReceipt receipt,
		DateTime time)
	{
		var log = FindLog(receipt, AvantisExtensions.OpenLimitPlacedTopic);
		if (log is null)
			return null;
		var pairIndex = ToInt32(AvantisExtensions.ReadAbiWord(log.Data, 0),
			"pair index");
		var tradeIndex = ToInt32(AvantisExtensions.ReadAbiWord(log.Data, 1),
			"trade index");
		return new()
		{
			PairIndex = pairIndex,
			TradeIndex = tradeIndex,
			Time = time,
		};
	}

	public AvantisMarketEvent TryGetMarketEvent(AvantisRpcReceipt receipt,
		DateTime fallbackTime)
	{
		var log = FindLog(receipt, AvantisExtensions.MarketOrderInitiatedTopic);
		if (log is null)
			return null;
		var timestamp = AvantisExtensions.ReadAbiWord(log.Data, 3);
		var time = timestamp > 0 && timestamp <= long.MaxValue
			? ((long)timestamp).FromUnix()
			: fallbackTime;
		return new()
		{
			PairIndex = ToInt32(AvantisExtensions.ReadAbiWord(log.Data, 0),
				"pair index"),
			OrderId = AvantisExtensions.ReadAbiWord(log.Data, 2)
				.ToString(CultureInfo.InvariantCulture),
			Time = time,
			IsBuy = AvantisExtensions.ReadAbiWord(log.Data, 4) != 0,
			IsPnl = AvantisExtensions.ReadAbiWord(log.Data, 5) != 0,
		};
	}

	public static decimal? GetCommission(AvantisRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var value = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return value.FromBaseUnits(18);
	}

	private async ValueTask<BigInteger> EstimateGasAsync(
		AvantisTransaction transaction, CancellationToken cancellationToken)
		=> (await SendAsync<AvantisRpcCallOnlyParameters, string>(
			"eth_estimateGas", new()
			{
				Call = ToRpcCall(transaction),
			}, true, cancellationToken)).ParseInteger();

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<AvantisRpcTagBooleanParameters,
				AvantisRpcBlock>("eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<AvantisRpcEmptyParameters,
				string>("eth_maxPriorityFeePerGas", new(), true,
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
		=> await SendAsync<AvantisRpcCallParameters, string>("eth_call",
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
		where TParameters : AvantisRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new AvantisRpcRequest<TParameters>
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
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					"Base JSON-RPC HTTP " + (int)response.StatusCode + ": " +
					Truncate(body));
			AvantisRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					AvantisRpcResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Base JSON-RPC returned an unexpected response shape.",
					error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(
					"Base JSON-RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(
					"Base JSON-RPC " + rpc.Error.Code + ": " +
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

	private AvantisRpcCall ToRpcCall(AvantisTransaction transaction)
		=> new()
		{
			From = WalletAddress,
			To = transaction.To.NormalizeAddress(),
			Data = transaction.Data,
			Value = transaction.Value.ToRpcHex(),
		};

	private static AvantisRpcLog FindLog(AvantisRpcReceipt receipt,
		string topic)
		=> (receipt?.Logs ?? []).FirstOrDefault(log => log is not null &&
			!log.IsRemoved && log.Address?.Equals(
				AvantisExtensions.TradingAddress,
				StringComparison.OrdinalIgnoreCase) == true &&
			log.Topics?.FirstOrDefault()?.Equals(topic,
				StringComparison.OrdinalIgnoreCase) == true);

	private static int ToInt32(BigInteger value, string name)
	{
		if (value < 0 || value > int.MaxValue)
			throw new InvalidDataException(
				"Avantis returned an invalid " + name + " '" + value + "'.");
		return (int)value;
	}

	private static void EnsureSuccessful(AvantisRpcReceipt receipt)
	{
		if (receipt is null)
			throw new InvalidDataException(
				"Base returned no Avantis transaction receipt.");
		if (receipt.Status.ParseInteger() == 0)
			throw new InvalidOperationException(
				"Avantis transaction '" + receipt.TransactionHash +
				"' reverted.");
	}

	private void EnsureWalletConfigured()
	{
		if (!IsWalletConfigured)
			throw new InvalidOperationException(
				"An Avantis wallet address is required for account data.");
	}

	private void EnsureSigningAvailable()
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Avantis transactions.");
	}

	private static void ValidateTransaction(AvantisTransaction transaction)
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
				"Avantis transaction calldata must be valid hex.");
		if (transaction.Value < 0)
			throw new InvalidDataException(
				"Avantis transaction value cannot be negative.");
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
				"Base JSON-RPC response exceeds the 8 MiB safety limit.");
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
					"Base JSON-RPC response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
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
}
