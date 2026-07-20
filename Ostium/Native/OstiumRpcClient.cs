namespace StockSharp.Ostium.Native;

sealed class OstiumRpcClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly OstiumNetwork _network;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly byte[] _privateKey;
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private long _requestId;
	private DateTime _nextRequest;

	public OstiumRpcClient(OstiumNetwork network, string endpoint,
		string walletAddress, SecureString privateKey)
	{
		_network = network ?? throw new ArgumentNullException(nameof(network));
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Ostium JSON-RPC endpoint must use HTTP or HTTPS.",
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
						"The configured Ostium wallet address does not match " +
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
			"StockSharp-Ostium-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Ostium_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsWalletConfigured => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public async ValueTask VerifyChainAsync(
		CancellationToken cancellationToken)
	{
		var chainId = (await SendAsync<OstiumRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != new BigInteger(_network.ChainId))
			throw new InvalidOperationException(
				"JSON-RPC is connected to chain " + chainId + ", but " +
				_network.Name + " chain " + _network.ChainId + " is required.");
	}

	public async ValueTask<BigInteger> GetEthBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		return (await SendAsync<OstiumRpcAddressTagParameters, string>(
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
		var data = OstiumExtensions.EncodeStaticCall("balanceOf(address)",
			OstiumExtensions.AbiAddress(WalletAddress));
		return OstiumExtensions.ReadAbiWord(await CallContractAsync(
			_network.UsdcAddress, data, cancellationToken), 0);
	}

	public async ValueTask<BigInteger> GetUsdcAllowanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var data = OstiumExtensions.EncodeStaticCall(
			"allowance(address,address)",
			OstiumExtensions.AbiAddress(WalletAddress),
			OstiumExtensions.AbiAddress(_network.TradingStorageAddress));
		return OstiumExtensions.ReadAbiWord(await CallContractAsync(
			_network.UsdcAddress, data, cancellationToken), 0);
	}

	public OstiumTransaction CreateApprovalTransaction(BigInteger amount)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return new()
		{
			To = _network.UsdcAddress,
			Data = OstiumExtensions.EncodeStaticCall("approve(address,uint256)",
				OstiumExtensions.AbiAddress(_network.TradingStorageAddress),
				OstiumExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public OstiumTransaction CreateOpenTradeTransaction(int pairIndex,
		Sides side, BigInteger collateral, BigInteger openPrice,
		BigInteger leverage, BigInteger takeProfit, BigInteger stopLoss,
		OstiumOpenOrderTypes orderType, bool isDayTrade,
		BigInteger slippageBps)
	{
		EnsureWalletConfigured();
		if (pairIndex is < 0 or > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		if (collateral <= 0 || openPrice <= 0 || leverage <= 0 ||
			takeProfit < 0 || stopLoss < 0 || slippageBps < 0)
			throw new ArgumentOutOfRangeException(nameof(collateral),
				"Ostium trade parameters are outside the supported range.");
		var data = "0x" + OstiumExtensions.AbiSelector(
			"openTrade((uint256,uint192,uint192,uint192,address,uint32,uint16,uint8,bool,bool),(address,uint32),uint8,uint256)") +
			OstiumExtensions.AbiWord(collateral) +
			OstiumExtensions.AbiWord(openPrice) +
			OstiumExtensions.AbiWord(takeProfit) +
			OstiumExtensions.AbiWord(stopLoss) +
			OstiumExtensions.AbiAddress(WalletAddress) +
			OstiumExtensions.AbiWord(leverage) +
			OstiumExtensions.AbiWord(pairIndex) +
			OstiumExtensions.AbiWord(BigInteger.Zero) +
			OstiumExtensions.AbiWord(side == Sides.Buy
				? BigInteger.One
				: BigInteger.Zero) +
			OstiumExtensions.AbiWord(isDayTrade
				? BigInteger.One
				: BigInteger.Zero) +
			OstiumExtensions.AbiAddress(OstiumExtensions.ZeroAddress) +
			OstiumExtensions.AbiWord(BigInteger.Zero) +
			OstiumExtensions.AbiWord((int)orderType) +
			OstiumExtensions.AbiWord(slippageBps);
		return new()
		{
			To = _network.TradingAddress,
			Data = data,
			Value = BigInteger.Zero,
		};
	}

	public OstiumTransaction CreateCloseTradeTransaction(int pairIndex,
		int positionIndex, BigInteger closePercentage, BigInteger price,
		BigInteger slippageBps)
	{
		if (pairIndex is < 0 or > ushort.MaxValue ||
			positionIndex is < 0 or > byte.MaxValue ||
			closePercentage <= 0 || closePercentage > 10000 || price <= 0 ||
			slippageBps < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = _network.TradingAddress,
			Data = OstiumExtensions.EncodeStaticCall(
				"closeTradeMarket(uint16,uint8,uint16,uint192,uint32)",
				OstiumExtensions.AbiWord(pairIndex),
				OstiumExtensions.AbiWord(positionIndex),
				OstiumExtensions.AbiWord(closePercentage),
				OstiumExtensions.AbiWord(price),
				OstiumExtensions.AbiWord(slippageBps)),
			Value = BigInteger.Zero,
		};
	}

	public OstiumTransaction CreateCancelLimitTransaction(int pairIndex,
		int positionIndex)
	{
		if (pairIndex is < 0 or > ushort.MaxValue ||
			positionIndex is < 0 or > byte.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = _network.TradingAddress,
			Data = OstiumExtensions.EncodeStaticCall(
				"cancelOpenLimitOrder(uint16,uint8)",
				OstiumExtensions.AbiWord(pairIndex),
				OstiumExtensions.AbiWord(positionIndex)),
			Value = BigInteger.Zero,
		};
	}

	public OstiumTransaction CreateUpdateLimitTransaction(int pairIndex,
		int positionIndex, BigInteger price, BigInteger takeProfit,
		BigInteger stopLoss)
	{
		if (pairIndex is < 0 or > ushort.MaxValue ||
			positionIndex is < 0 or > byte.MaxValue || price <= 0 ||
			takeProfit < 0 || stopLoss < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return new()
		{
			To = _network.TradingAddress,
			Data = OstiumExtensions.EncodeStaticCall(
				"updateOpenLimitOrder(uint16,uint8,uint192,uint192,uint192)",
				OstiumExtensions.AbiWord(pairIndex),
				OstiumExtensions.AbiWord(positionIndex),
				OstiumExtensions.AbiWord(price),
				OstiumExtensions.AbiWord(takeProfit),
				OstiumExtensions.AbiWord(stopLoss)),
			Value = BigInteger.Zero,
		};
	}

	public async ValueTask<OstiumRpcReceipt> SendAndWaitAsync(
		OstiumTransaction transaction, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var hash = await SendTransactionAsync(transaction, cancellationToken);
		var receipt = await WaitForReceiptAsync(hash, timeout,
			cancellationToken);
		EnsureSuccessful(receipt);
		return receipt;
	}

	public async ValueTask<string> SendTransactionAsync(
		OstiumTransaction transaction, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<OstiumRpcAddressTagParameters,
				string>("eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger();
			var estimated = await EstimateGasAsync(transaction,
				cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"The Ostium transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(new BigInteger(_network.ChainId),
					nonce, eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<OstiumRpcEmptyParameters,
					string>("eth_gasPrice", new(), true,
					cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(_network.ChainId).ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<OstiumRpcValueParameters, string>(
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

	public ValueTask<OstiumRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<OstiumRpcValueParameters, OstiumRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<OstiumRpcReceipt> WaitForReceiptAsync(string hash,
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
			"Ostium transaction '" + hash + "' was not mined within " +
			timeout + ".");
	}

	public async ValueTask<DateTime> GetReceiptTimeAsync(
		OstiumRpcReceipt receipt, CancellationToken cancellationToken)
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
		var block = await SendAsync<OstiumRpcTagBooleanParameters,
			OstiumRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken) ?? throw new InvalidDataException(
				_network.Name + " returned no requested block.");
		var seconds = block.Timestamp.ParseInteger();
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				_network.Name + " returned an invalid block timestamp.");
		return ((long)seconds).FromUnix().EnsureOstiumUtc();
	}

	public OstiumLimitEvent TryGetLimitEvent(OstiumRpcReceipt receipt,
		DateTime time)
	{
		var log = FindLog(receipt, OstiumExtensions.OpenLimitPlacedV2Topic) ??
			FindLog(receipt, OstiumExtensions.OpenLimitPlacedTopic);
		if (log is null || log.Topics?.Length < 3)
			return null;
		return new()
		{
			PairIndex = ToInt32(log.Topics[2].ParseInteger(), "pair index"),
			PositionIndex = ToInt32(OstiumExtensions.ReadAbiWord(log.Data, 0),
				"position index"),
			Time = time.EnsureOstiumUtc(),
		};
	}

	public OstiumMarketEvent TryGetMarketOpenEvent(OstiumRpcReceipt receipt,
		DateTime time)
	{
		var log = FindLog(receipt,
			OstiumExtensions.MarketOpenOrderInitiatedTopic);
		if (log is null || log.Topics?.Length < 4)
			return null;
		return new()
		{
			PairIndex = ToInt32(log.Topics[3].ParseInteger(), "pair index"),
			OrderId = log.Topics[1].ParseInteger().ToString(
				CultureInfo.InvariantCulture),
			Time = time.EnsureOstiumUtc(),
		};
	}

	public OstiumMarketEvent TryGetMarketCloseEvent(OstiumRpcReceipt receipt,
		DateTime time)
	{
		var log = FindLog(receipt,
			OstiumExtensions.MarketCloseOrderInitiatedV2Topic);
		if (log is null || log.Topics?.Length < 2)
			return null;
		return new()
		{
			PairIndex = ToInt32(OstiumExtensions.ReadAbiWord(log.Data, 0),
				"pair index"),
			OrderId = log.Topics[1].ParseInteger().ToString(
				CultureInfo.InvariantCulture),
			Time = time.EnsureOstiumUtc(),
		};
	}

	public static decimal? GetCommission(OstiumRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var value = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return value.FromBaseUnits(18);
	}

	private async ValueTask<BigInteger> EstimateGasAsync(
		OstiumTransaction transaction, CancellationToken cancellationToken)
		=> (await SendAsync<OstiumRpcCallOnlyParameters, string>(
			"eth_estimateGas", new()
			{
				Call = ToRpcCall(transaction),
			}, true, cancellationToken)).ParseInteger();

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<OstiumRpcTagBooleanParameters,
				OstiumRpcBlock>("eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<OstiumRpcEmptyParameters,
				string>("eth_maxPriorityFeePerGas", new(), true,
				cancellationToken)).ParseInteger();
			if (baseFee <= 0 || priority < 0)
				return null;
			return (priority, baseFee * 2 + priority);
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
	}

	private async ValueTask<string> CallContractAsync(string address,
		string data, CancellationToken cancellationToken)
		=> await SendAsync<OstiumRpcCallParameters, string>("eth_call",
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
		where TParameters : OstiumRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new OstiumRpcRequest<TParameters>
			{
				Id = requestId,
				Method = method,
				Parameters = parameters,
			}, _settings);
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
					_network.Name + " JSON-RPC HTTP " +
					(int)response.StatusCode + ": " + Truncate(body));
			OstiumRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<OstiumRpcResponse<TResult>>(
					body, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					_network.Name +
					" JSON-RPC returned an unexpected response shape.", error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(
					_network.Name +
					" JSON-RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(
					_network.Name + " JSON-RPC " + rpc.Error.Code + ": " +
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

	private OstiumRpcCall ToRpcCall(OstiumTransaction transaction)
		=> new()
		{
			From = WalletAddress,
			To = transaction.To.NormalizeAddress(),
			Data = transaction.Data,
			Value = transaction.Value.ToRpcHex(),
		};

	private OstiumRpcLog FindLog(OstiumRpcReceipt receipt, string topic)
		=> (receipt?.Logs ?? []).FirstOrDefault(log => log is not null &&
			!log.IsRemoved && log.Address?.Equals(_network.TradingAddress,
				StringComparison.OrdinalIgnoreCase) == true &&
			log.Topics?.FirstOrDefault()?.Equals(topic,
				StringComparison.OrdinalIgnoreCase) == true);

	private static int ToInt32(BigInteger value, string name)
	{
		if (value < 0 || value > int.MaxValue)
			throw new InvalidDataException(
				"Ostium returned an invalid " + name + " '" + value + "'.");
		return (int)value;
	}

	private static void EnsureSuccessful(OstiumRpcReceipt receipt)
	{
		if (receipt is null)
			throw new InvalidDataException(
				"Ostium returned no transaction receipt.");
		if (receipt.Status.ParseInteger() == 0)
			throw new InvalidOperationException(
				"Ostium transaction '" + receipt.TransactionHash +
				"' reverted.");
	}

	private void EnsureWalletConfigured()
	{
		if (!IsWalletConfigured)
			throw new InvalidOperationException(
				"An Ostium wallet address is required for account data.");
	}

	private void EnsureSigningAvailable()
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Ostium transactions.");
	}

	private static void ValidateTransaction(OstiumTransaction transaction)
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
				"Ostium transaction calldata must be valid hex.");
		if (transaction.Value < 0)
			throw new InvalidDataException(
				"Ostium transaction value cannot be negative.");
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
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
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
