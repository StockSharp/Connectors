namespace StockSharp.GainsNetwork.Native;

sealed class GainsNetworkRpcClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private const string _openOrderPlacedTopic =
		"0xb57382e21e3ceb31b5beda26d7cc7e459dc52a0b1f5ae0c9b4e603401b7dc642";
	private const string _marketOrderInitiatedTopic =
		"0x3a60290d7335bce64a807e90f39655517bb5fa702423fa8fac283a5ea16d3a97";
	private readonly GainsNetworkDeployment _deployment;
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

	public GainsNetworkRpcClient(GainsNetworkDeployment deployment,
		string endpoint, string walletAddress, SecureString privateKey)
	{
		_deployment = deployment ?? throw new ArgumentNullException(
			nameof(deployment));
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Gains JSON-RPC endpoint must use HTTP or HTTPS.",
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
						"The configured Gains wallet address does not match " +
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
			"StockSharp-GainsNetwork-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "GainsNetwork_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsWalletConfigured => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public async ValueTask VerifyChainAsync(
		CancellationToken cancellationToken)
	{
		var chainId = (await SendAsync<GainsRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger(
				"chain ID");
		if (chainId != new BigInteger(_deployment.ChainId))
			throw new InvalidOperationException(
				"JSON-RPC is connected to chain " + chainId + ", but " +
				_deployment.Name + " chain " + _deployment.ChainId +
				" is required.");
	}

	public async ValueTask<BigInteger> GetNativeBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		return (await SendAsync<GainsRpcAddressTagParameters, string>(
			"eth_getBalance", new()
			{
				Address = WalletAddress,
				BlockTag = "latest",
			}, true, cancellationToken)).ParseInteger("native balance");
	}

	public async ValueTask<BigInteger> GetTokenBalanceAsync(string token,
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var data = GainsNetworkExtensions.EncodeStaticCall("balanceOf(address)",
			GainsNetworkExtensions.AbiAddress(WalletAddress));
		return GainsNetworkExtensions.ReadAbiWord(await CallContractAsync(token,
			data, cancellationToken), 0);
	}

	public async ValueTask<BigInteger> GetTokenAllowanceAsync(string token,
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var data = GainsNetworkExtensions.EncodeStaticCall(
			"allowance(address,address)",
			GainsNetworkExtensions.AbiAddress(WalletAddress),
			GainsNetworkExtensions.AbiAddress(_deployment.DiamondAddress));
		return GainsNetworkExtensions.ReadAbiWord(await CallContractAsync(token,
			data, cancellationToken), 0);
	}

	public GainsTransaction CreateApprovalTransaction(string token,
		BigInteger amount)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return new()
		{
			To = token.NormalizeAddress(),
			Data = GainsNetworkExtensions.EncodeStaticCall(
				"approve(address,uint256)",
				GainsNetworkExtensions.AbiAddress(_deployment.DiamondAddress),
				GainsNetworkExtensions.AbiWord(amount)),
			Value = BigInteger.Zero,
		};
	}

	public GainsTransaction CreateOpenTradeTransaction(int pairIndex,
		Sides side, int collateralIndex, BigInteger collateralAmount,
		BigInteger openPrice, BigInteger leverage, BigInteger takeProfit,
		BigInteger stopLoss, GainsTradeTypes tradeType,
		BigInteger maximumSlippagePercentage, string referrer)
	{
		EnsureWalletConfigured();
		if (pairIndex is < 0 or > ushort.MaxValue ||
			collateralIndex is < 1 or > byte.MaxValue ||
			collateralAmount <= 0 || collateralAmount >= BigInteger.Pow(2, 120) ||
			openPrice <= 0 || openPrice > ulong.MaxValue ||
			leverage <= 0 || leverage >= BigInteger.Pow(2, 24) ||
			takeProfit < 0 || takeProfit > ulong.MaxValue ||
			stopLoss < 0 || stopLoss > ulong.MaxValue ||
			maximumSlippagePercentage < 0 ||
			maximumSlippagePercentage > ushort.MaxValue ||
			!Enum.IsDefined(tradeType))
			throw new ArgumentOutOfRangeException(nameof(collateralAmount),
				"Gains trade parameters are outside the contract ranges.");
		referrer = referrer.IsEmpty()
			? GainsNetworkExtensions.ZeroAddress
			: referrer.NormalizeAddress();
		var data = "0x" + GainsNetworkExtensions.AbiSelector(
			"openTrade((address,uint32,uint16,uint24,bool,bool,uint8,uint8," +
			"uint120,uint64,uint64,uint64,uint192),uint16,address)") +
			GainsNetworkExtensions.AbiAddress(WalletAddress) +
			GainsNetworkExtensions.AbiWord(BigInteger.Zero) +
			GainsNetworkExtensions.AbiWord(pairIndex) +
			GainsNetworkExtensions.AbiWord(leverage) +
			GainsNetworkExtensions.AbiWord(side == Sides.Buy
				? BigInteger.One
				: BigInteger.Zero) +
			GainsNetworkExtensions.AbiWord(BigInteger.One) +
			GainsNetworkExtensions.AbiWord(collateralIndex) +
			GainsNetworkExtensions.AbiWord((int)tradeType) +
			GainsNetworkExtensions.AbiWord(collateralAmount) +
			GainsNetworkExtensions.AbiWord(openPrice) +
			GainsNetworkExtensions.AbiWord(takeProfit) +
			GainsNetworkExtensions.AbiWord(stopLoss) +
			GainsNetworkExtensions.AbiWord(BigInteger.Zero) +
			GainsNetworkExtensions.AbiWord(maximumSlippagePercentage) +
			GainsNetworkExtensions.AbiAddress(referrer);
		return new()
		{
			To = _deployment.DiamondAddress,
			Data = data,
			Value = BigInteger.Zero,
		};
	}

	public GainsTransaction CreateCloseTradeTransaction(int tradeIndex,
		BigInteger expectedPrice)
	{
		if (tradeIndex < 0 || expectedPrice <= 0 ||
			expectedPrice > ulong.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(tradeIndex));
		return CreateDiamondTransaction(
			"closeTradeMarket(uint32,uint64)",
			GainsNetworkExtensions.AbiWord(tradeIndex),
			GainsNetworkExtensions.AbiWord(expectedPrice));
	}

	public GainsTransaction CreateCancelOpenOrderTransaction(int tradeIndex)
	{
		if (tradeIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(tradeIndex));
		return CreateDiamondTransaction("cancelOpenOrder(uint32)",
			GainsNetworkExtensions.AbiWord(tradeIndex));
	}

	public GainsTransaction CreateUpdateOpenOrderTransaction(int tradeIndex,
		BigInteger triggerPrice, BigInteger takeProfit, BigInteger stopLoss,
		BigInteger maximumSlippagePercentage)
	{
		if (tradeIndex < 0 || triggerPrice <= 0 ||
			triggerPrice > ulong.MaxValue || takeProfit < 0 ||
			takeProfit > ulong.MaxValue || stopLoss < 0 ||
			stopLoss > ulong.MaxValue || maximumSlippagePercentage < 0 ||
			maximumSlippagePercentage > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(tradeIndex));
		return CreateDiamondTransaction(
			"updateOpenOrder(uint32,uint64,uint64,uint64,uint16)",
			GainsNetworkExtensions.AbiWord(tradeIndex),
			GainsNetworkExtensions.AbiWord(triggerPrice),
			GainsNetworkExtensions.AbiWord(takeProfit),
			GainsNetworkExtensions.AbiWord(stopLoss),
			GainsNetworkExtensions.AbiWord(maximumSlippagePercentage));
	}

	private GainsTransaction CreateDiamondTransaction(string signature,
		params string[] words)
		=> new()
		{
			To = _deployment.DiamondAddress,
			Data = GainsNetworkExtensions.EncodeStaticCall(signature, words),
			Value = BigInteger.Zero,
		};

	public async ValueTask<GainsRpcReceipt> SendAndWaitAsync(
		GainsTransaction transaction, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var hash = await SendTransactionAsync(transaction, cancellationToken);
		var receipt = await WaitForReceiptAsync(hash, timeout,
			cancellationToken);
		EnsureSuccessful(receipt);
		return receipt;
	}

	public async ValueTask<string> SendTransactionAsync(
		GainsTransaction transaction, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<GainsRpcAddressTagParameters, string>(
				"eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger("transaction nonce");
			var estimated = await EstimateGasAsync(transaction, cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"The Gains transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(
					new BigInteger(_deployment.ChainId), nonce,
					eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<GainsRpcEmptyParameters, string>(
					"eth_gasPrice", new(), true, cancellationToken)).ParseInteger(
						"gas price");
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(_deployment.ChainId)
						.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			var hash = await SendAsync<GainsRpcValueParameters, string>(
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

	public ValueTask<GainsRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<GainsRpcValueParameters, GainsRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<GainsRpcReceipt> WaitForReceiptAsync(string hash,
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
		throw new TimeoutException("Gains transaction '" + hash +
			"' was not mined within " + timeout + ".");
	}

	public async ValueTask<DateTime> GetReceiptTimeAsync(
		GainsRpcReceipt receipt, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		return await GetBlockTimeAsync(receipt.BlockNumber.ParseInteger(
			"receipt block"), cancellationToken);
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(BigInteger blockNumber,
		CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var block = await SendAsync<GainsRpcTagBooleanParameters,
			GainsRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = blockNumber.ToRpcHex(),
				IsTransactionsIncluded = false,
			}, true, cancellationToken) ?? throw new InvalidDataException(
				_deployment.Name + " returned no requested block.");
		var seconds = block.Timestamp.ParseInteger("block timestamp");
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				_deployment.Name + " returned an invalid block timestamp.");
		return ((long)seconds).FromUnix().EnsureUtc();
	}

	public GainsTransactionEvent TryGetOpenOrderEvent(GainsRpcReceipt receipt)
	{
		var log = FindLog(receipt, _openOrderPlacedTopic);
		if (log?.Topics?.Length < 4)
			return null;
		return new()
		{
			PairIndex = ToInt32(log.Topics[2].ParseInteger("pair index"),
				"pair index"),
			OrderIndex = ToInt32(log.Topics[3].ParseInteger("order index"),
				"order index"),
		};
	}

	public GainsTransactionEvent TryGetMarketOrderEvent(
		GainsRpcReceipt receipt)
	{
		var log = FindLog(receipt, _marketOrderInitiatedTopic);
		if (log?.Topics?.Length < 3)
			return null;
		return new()
		{
			PairIndex = ToInt32(log.Topics[2].ParseInteger("pair index"),
				"pair index"),
			OrderIndex = ToInt32(GainsNetworkExtensions.ReadAbiWord(log.Data, 1),
				"pending order index"),
		};
	}

	public static decimal? GetCommission(GainsRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var value = receipt.GasUsed.ParseInteger("gas used") *
			receipt.EffectiveGasPrice.ParseInteger("effective gas price");
		return value.FromBaseUnits(18);
	}

	private async ValueTask<BigInteger> EstimateGasAsync(
		GainsTransaction transaction, CancellationToken cancellationToken)
		=> (await SendAsync<GainsRpcCallOnlyParameters, string>(
			"eth_estimateGas", new()
			{
				Call = ToRpcCall(transaction),
			}, true, cancellationToken)).ParseInteger("gas estimate");

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<GainsRpcTagBooleanParameters,
				GainsRpcBlock>("eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger("base fee");
			var priority = (await SendAsync<GainsRpcEmptyParameters, string>(
				"eth_maxPriorityFeePerGas", new(), true,
				cancellationToken)).ParseInteger("priority fee");
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
		=> await SendAsync<GainsRpcCallParameters, string>("eth_call", new()
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
		where TParameters : GainsRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new GainsRpcRequest<TParameters>
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
				throw new InvalidOperationException(_deployment.Name +
					" JSON-RPC HTTP " + (int)response.StatusCode + ": " +
					Truncate(body));
			GainsRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<GainsRpcResponse<TResult>>(
					body, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(_deployment.Name +
					" JSON-RPC returned an unexpected response shape.", error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(_deployment.Name +
					" JSON-RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(_deployment.Name +
					" JSON-RPC " + rpc.Error.Code + ": " +
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

	private GainsRpcCall ToRpcCall(GainsTransaction transaction)
		=> new()
		{
			From = WalletAddress,
			To = transaction.To.NormalizeAddress(),
			Data = transaction.Data,
			Value = transaction.Value.ToRpcHex(),
		};

	private GainsRpcLog FindLog(GainsRpcReceipt receipt, string topic)
		=> (receipt?.Logs ?? []).FirstOrDefault(log => log is not null &&
			!log.IsRemoved && log.Address?.Equals(_deployment.DiamondAddress,
				StringComparison.OrdinalIgnoreCase) == true &&
			log.Topics?.FirstOrDefault()?.Equals(topic,
				StringComparison.OrdinalIgnoreCase) == true);

	private static int ToInt32(BigInteger value, string name)
	{
		if (value < 0 || value > int.MaxValue)
			throw new InvalidDataException("Gains " + name +
				" is outside the supported range.");
		return (int)value;
	}

	private static void ValidateTransaction(GainsTransaction transaction)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		_ = transaction.To.NormalizeAddress();
		if (transaction.Data.IsEmpty() || !transaction.Data.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) ||
			transaction.Data.Length % 2 != 0 ||
			transaction.Data[2..].Any(static ch => !Uri.IsHexDigit(ch)) ||
			transaction.Value < 0)
			throw new ArgumentException("Invalid Gains transaction payload.",
				nameof(transaction));
	}

	private void EnsureWalletConfigured()
	{
		if (!IsWalletConfigured)
			throw new InvalidOperationException(
				"A Gains EVM wallet address is required.");
	}

	private void EnsureSigningAvailable()
	{
		EnsureWalletConfigured();
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Gains EVM private key is required for transactions.");
	}

	private static void EnsureSuccessful(GainsRpcReceipt receipt)
	{
		if (receipt is null)
			throw new InvalidDataException(
				"Gains returned an empty transaction receipt.");
		if (receipt.Status.ParseInteger("receipt status") != BigInteger.One)
			throw new InvalidOperationException("Gains transaction '" +
				receipt.TransactionHash + "' reverted.");
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
				"Gains JSON-RPC response exceeds the 8 MiB safety limit.");
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
					"Gains JSON-RPC response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
