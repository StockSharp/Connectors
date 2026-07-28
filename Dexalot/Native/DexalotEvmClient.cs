namespace StockSharp.Dexalot.Native;

sealed class DexalotEvmClient : BaseLogReceiver
{
	private const int _chainId = 432204;
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private const string _orderStatusSignature =
		"OrderStatusChanged(uint8,address,bytes32,(bytes32,bytes32,bytes32," +
		"uint256,uint256,uint256,uint256,uint256,address,uint8,uint8,uint8," +
		"uint8,uint32,uint32),uint32,bytes32)";
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

	public DexalotEvmClient(string endpoint, string walletAddress,
		SecureString privateKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Dexalot JSON-RPC endpoint must be an absolute HTTP or HTTPS " +
					"URI.", nameof(endpoint));
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
						"The configured Dexalot wallet does not match the " +
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
				? DexalotExtensions.ProbeAddress
				: walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Dexalot-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Dexalot_L1_JSON_RPC";

	public string WalletAddress { get; }

	public bool IsSigningAvailable => _privateKey is not null;

	public bool IsWalletConfigured => !WalletAddress.EqualsIgnoreCase(
		DexalotExtensions.ProbeAddress);

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
		var chainId = (await SendAsync<string>("eth_chainId", [],
			true, cancellationToken)).ParseInteger();
		if (chainId != _chainId)
			throw new InvalidOperationException(
				$"Dexalot RPC is connected to chain {chainId}, expected " +
					$"{_chainId}.");
	}

	public async ValueTask<DexalotBook> GetBookAsync(string contractAddress,
		DexalotPair pair, int depth, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		depth = depth.Max(1).Min(5000);
		var bids = await GetBookSideAsync(contractAddress, pair, 0, depth,
			cancellationToken);
		var asks = await GetBookSideAsync(contractAddress, pair, 1, depth,
			cancellationToken);
		return new()
		{
			Bids = bids.OrderByDescending(static level => level.Price)
				.ToArray(),
			Asks = asks.OrderBy(static level => level.Price).ToArray(),
			Time = DateTime.UtcNow,
		};
	}

	public async ValueTask<(BigInteger Total, BigInteger Available)>
		GetBalanceAsync(string portfolioAddress, string owner, string symbol,
		CancellationToken cancellationToken)
	{
		var data = DexalotExtensions.EncodeCall(
			"getBalance(address,bytes32)",
			DexalotExtensions.AbiAddress(owner),
			DexalotExtensions.AbiBytes32(symbol));
		var result = await CallAsync(portfolioAddress, data,
			cancellationToken);
		return (DexalotExtensions.ReadWord(result, 0),
			DexalotExtensions.ReadWord(result, 1));
	}

	public async ValueTask<string> SendOrderAsync(string contractAddress,
		DexalotPair pair, long transactionId, Sides side,
		OrderTypes orderType, int type2, int stp, decimal price,
		decimal volume, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ArgumentNullException.ThrowIfNull(pair);
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				$"Dexalot order type '{orderType}' is not supported.");
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		if (type2 is < 0 or > 3)
			throw new ArgumentOutOfRangeException(nameof(type2));
		if (stp is < 0 or > 3)
			throw new ArgumentOutOfRangeException(nameof(stp));
		var clientOrderId = CreateClientOrderId(transactionId);
		var data = DexalotExtensions.EncodeCall(
			"addNewOrder((bytes32,bytes32,uint256,uint256,address,uint8," +
				"uint8,uint8,uint8))",
			clientOrderId[2..],
			DexalotExtensions.AbiBytes32(pair.Pair),
			DexalotExtensions.AbiWord(orderType == OrderTypes.Market
				? BigInteger.Zero
				: price.ToBaseUnits(pair.QuoteDecimals)),
			DexalotExtensions.AbiWord(
				volume.ToBaseUnits(pair.BaseDecimals)),
			DexalotExtensions.AbiAddress(WalletAddress),
			DexalotExtensions.AbiWord(side == Sides.Buy ? 0 : 1),
			DexalotExtensions.AbiWord(
				orderType == OrderTypes.Market ? 0 : 1),
			DexalotExtensions.AbiWord(type2),
			DexalotExtensions.AbiWord(stp));
		return await SendTransactionAsync(new()
		{
			To = contractAddress,
			Data = data,
		}, cancellationToken);
	}

	public ValueTask<string> CancelOrderAsync(string contractAddress,
		string orderId, CancellationToken cancellationToken)
		=> SendTransactionAsync(new()
		{
			To = contractAddress,
			Data = DexalotExtensions.EncodeCall("cancelOrder(bytes32)",
				NormalizeBytes32(orderId)[2..]),
		}, cancellationToken);

	public ValueTask<string> ReplaceOrderAsync(string contractAddress,
		DexalotPair pair, long transactionId, string orderId, decimal price,
		decimal volume, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		return SendTransactionAsync(new()
		{
			To = contractAddress,
			Data = DexalotExtensions.EncodeCall(
				"cancelReplaceOrder(bytes32,bytes32,uint256,uint256)",
				NormalizeBytes32(orderId)[2..],
				CreateClientOrderId(transactionId)[2..],
				DexalotExtensions.AbiWord(
					price.ToBaseUnits(pair.QuoteDecimals)),
				DexalotExtensions.AbiWord(
					volume.ToBaseUnits(pair.BaseDecimals))),
		}, cancellationToken);
	}

	public async ValueTask<DexalotReceipt> WaitForReceiptAsync(string hash,
		TimeSpan timeout, CancellationToken cancellationToken)
	{
		hash = hash.NormalizeHash();
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var receipt = await SendAsync<DexalotReceipt>(
				"eth_getTransactionReceipt", [hash], true,
				cancellationToken);
			if (receipt is not null)
			{
				if (!receipt.TransactionHash.IsEmpty() &&
					!receipt.TransactionHash.NormalizeHash()
						.EqualsIgnoreCase(hash))
					throw new InvalidDataException(
						"Dexalot RPC returned a receipt for a different " +
							"transaction.");
				return receipt;
			}
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
		}
		throw new TimeoutException(
			$"Dexalot transaction '{hash}' was not mined within {timeout}.");
	}

	public async ValueTask<DateTime> GetBlockTimeAsync(string blockNumber,
		CancellationToken cancellationToken)
	{
		var block = await SendAsync<DexalotBlock>("eth_getBlockByNumber",
			[blockNumber, false], true, cancellationToken);
		if (block?.Timestamp.IsEmpty() != false)
			throw new InvalidDataException(
				$"Dexalot block '{blockNumber}' has no timestamp.");
		return DateTimeOffset.FromUnixTimeSeconds(
			(long)block.Timestamp.ParseInteger()).UtcDateTime;
	}

	public DexalotOrderEvent ParseOrderEvent(DexalotReceipt receipt,
		DexalotPair pair, string contractAddress)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		ArgumentNullException.ThrowIfNull(pair);
		var topic = "0x" + new Sha3Keccack().CalculateHash(
			_orderStatusSignature);
		var log = receipt.Logs?.LastOrDefault(item =>
			item.Address?.EqualsIgnoreCase(contractAddress) == true &&
			item.Topics is { Length: >= 3 } &&
			item.Topics[0].EqualsIgnoreCase(topic));
		if (log is null)
			return null;
		var pairFromTopic = Encoding.UTF8.GetString(
			log.Topics[2][2..].HexToByteArray()).TrimEnd('\0');
		if (!pairFromTopic.EqualsIgnoreCase(pair.Pair))
			throw new InvalidDataException(
				"Dexalot receipt contains an order for a different pair.");
		var status = (int)DexalotExtensions.ReadWord(log.Data, 13);
		return new()
		{
			OrderId = DexalotExtensions.ReadBytes32(log.Data, 1),
			ClientOrderId = DexalotExtensions.ReadBytes32(log.Data, 2),
			Pair = pairFromTopic,
			Price = DexalotExtensions.ReadWord(log.Data, 4)
				.FromBaseUnits(pair.QuoteDecimals),
			Quantity = DexalotExtensions.ReadWord(log.Data, 6)
				.FromBaseUnits(pair.BaseDecimals),
			FilledVolume = DexalotExtensions.ReadWord(log.Data, 7)
				.FromBaseUnits(pair.BaseDecimals),
			Side = DexalotExtensions.ReadWord(log.Data, 10) == 0
				? Sides.Buy
				: Sides.Sell,
			OrderType = DexalotExtensions.ReadWord(log.Data, 11) == 0
				? OrderTypes.Market
				: OrderTypes.Limit,
			Type2 = (int)DexalotExtensions.ReadWord(log.Data, 12),
			Status = status,
			Code = DexalotExtensions.ReadBytes32Text(log.Data, 17),
		};
	}

	internal static string CreateClientOrderId(long transactionId)
	{
		if (transactionId <= 0)
			throw new ArgumentOutOfRangeException(nameof(transactionId));
		var value = new BigInteger(transactionId);
		return "0x" + DexalotExtensions.AbiWord(value);
	}

	private async ValueTask<DexalotBookLevel[]> GetBookSideAsync(
		string contractAddress, DexalotPair pair, int side, int depth,
		CancellationToken cancellationToken)
	{
		var data = DexalotExtensions.EncodeCall(
			"getNBook(bytes32,uint8,uint256,uint256,uint256,bytes32)",
			DexalotExtensions.AbiBytes32(pair.Pair),
			DexalotExtensions.AbiWord(side),
			DexalotExtensions.AbiWord(depth),
			DexalotExtensions.AbiWord(100),
			DexalotExtensions.AbiWord(BigInteger.Zero),
			DexalotExtensions.AbiWord(BigInteger.Zero));
		var result = await CallAsync(contractAddress, data,
			cancellationToken);
		var prices = DexalotExtensions.ReadDynamicUIntArray(result, 0);
		var quantities = DexalotExtensions.ReadDynamicUIntArray(result, 1);
		if (prices.Length != quantities.Length)
			throw new InvalidDataException(
				"Dexalot contract returned mismatched book arrays.");
		return [.. prices.Zip(quantities, (price, volume) => new
			DexalotBookLevel
			{
				Price = price.FromBaseUnits(pair.QuoteDecimals),
				Volume = volume.FromBaseUnits(pair.BaseDecimals),
			}).Where(static level => level.Price > 0 && level.Volume > 0)];
	}

	private async ValueTask<string> CallAsync(string address, string data,
		CancellationToken cancellationToken)
	{
		var call = new JObject
		{
			["from"] = WalletAddress,
			["to"] = address.NormalizeAddress(),
			["data"] = data,
		};
		return await SendAsync<string>("eth_call", [call, "latest"], true,
			cancellationToken);
	}

	private async ValueTask<string> SendTransactionAsync(
		DexalotTransaction transaction,
		CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ArgumentNullException.ThrowIfNull(transaction);
		var to = transaction.To.NormalizeAddress();
		if (transaction.Data.IsEmpty() ||
			!transaction.Data.StartsWith("0x",
				StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("Invalid Dexalot transaction data.");
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<string>(
				"eth_getTransactionCount",
				[WalletAddress, "pending"], true, cancellationToken))
				.ParseInteger();
			var call = new JObject
			{
				["from"] = WalletAddress,
				["to"] = to,
				["data"] = transaction.Data,
				["value"] = "0x0",
			};
			var estimated = (await SendAsync<string>("eth_estimateGas",
				[call], true, cancellationToken)).ParseInteger();
			if (estimated <= 0)
				throw new InvalidDataException(
					"Dexalot transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var block = await SendAsync<DexalotBlock>(
				"eth_getBlockByNumber", ["latest", false], true,
				cancellationToken);
			byte[] encoded;
			if (block?.BaseFeePerGas.IsEmpty() == false)
			{
				var baseFee = block.BaseFeePerGas.ParseInteger();
				var priority = (await SendAsync<string>(
					"eth_maxPriorityFeePerGas", [], true,
					cancellationToken)).ParseInteger();
				var tx = new Transaction1559(_chainId, nonce, priority,
					baseFee * 2 + priority, gasLimit, to, BigInteger.Zero,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<string>("eth_gasPrice", [],
					true, cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					to.HexToByteArray(),
					BigInteger.Zero.ToBytesForRLPEncoding(),
					transaction.Data[2..].HexToByteArray(),
					new BigInteger(_chainId).ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			return (await SendAsync<string>("eth_sendRawTransaction",
				[encoded.ToHex(true)], false, cancellationToken))
				.NormalizeHash();
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	private async ValueTask<T> SendAsync<T>(string method,
		JArray parameters, bool isRead,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(new JObject
		{
			["jsonrpc"] = "2.0",
			["id"] = id,
			["method"] = method,
			["params"] = parameters ?? [],
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
					$"Dexalot RPC HTTP {(int)response.StatusCode}: " +
						Truncate(body));
			DexalotRpcResponse<T> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<DexalotRpcResponse<T>>(
					body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Dexalot RPC returned an unexpected payload.", error);
			}
			if (rpc is null || rpc.Id != id)
				throw new InvalidDataException(
					"Dexalot RPC returned an invalid response identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(
					$"Dexalot RPC {rpc.Error.Code}: " +
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

	private void EnsureSigningAvailable()
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Dexalot private key is required for trading.");
	}

	private static string NormalizeBytes32(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			value.Length != 66 ||
			value[2..].Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid bytes32 value '{value}'.", nameof(value));
		return "0x" + value[2..].ToLowerInvariant();
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Dexalot RPC response exceeds the 8 MiB safety limit.");
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
					"Dexalot RPC response exceeds the 8 MiB safety limit.");
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
