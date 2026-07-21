namespace StockSharp.SynFutures.Native;

sealed class SynFuturesRpcClient : BaseLogReceiver
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

	public SynFuturesRpcClient(string endpoint, string walletAddress,
		SecureString privateKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"SynFutures JSON-RPC endpoint must use HTTP or HTTPS.",
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
						"The configured SynFutures wallet address does not " +
						"match the private key.", nameof(walletAddress));
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
			"StockSharp-SynFutures-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "SynFutures_JSON-RPC";

	public string WalletAddress { get; }

	public bool IsWalletConfigured => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public async ValueTask VerifyChainAsync(
		CancellationToken cancellationToken)
	{
		var chainId = (await SendAsync<SynFuturesRpcEmptyParameters, string>(
			"eth_chainId", new(), true, cancellationToken)).ParseInteger();
		if (chainId != SynFuturesExtensions.ChainId)
			throw new InvalidOperationException(
				"JSON-RPC is connected to chain " + chainId +
				", but Base chain " + SynFuturesExtensions.ChainId +
				" is required.");
	}

	public async ValueTask<BigInteger> GetEthBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		return (await SendAsync<SynFuturesRpcAddressTagParameters, string>(
			"eth_getBalance", new()
			{
				Address = WalletAddress,
				BlockTag = "latest",
			}, true, cancellationToken)).ParseInteger();
	}

	public SynFuturesTransaction CreateTradeTransaction(
		SynFuturesMarket market, BigInteger signedSize, BigInteger margin,
		int limitTick, uint deadline)
	{
		ValidateMarket(market);
		if (signedSize == 0)
			throw new ArgumentOutOfRangeException(nameof(signedSize));
		if (margin < 0)
			throw new ArgumentOutOfRangeException(nameof(margin));
		return new()
		{
			To = market.InstrumentAddress.NormalizeAddress(),
			Data = SynFuturesExtensions.EncodeTradeCall(market.Expiry,
				signedSize, margin, limitTick, deadline),
			Value = BigInteger.Zero,
		};
	}

	public SynFuturesTransaction CreatePlaceTransaction(
		SynFuturesMarket market, BigInteger signedSize, BigInteger margin,
		int tick, uint deadline)
	{
		ValidateMarket(market);
		if (signedSize == 0)
			throw new ArgumentOutOfRangeException(nameof(signedSize));
		if (margin <= 0)
			throw new ArgumentOutOfRangeException(nameof(margin));
		return new()
		{
			To = market.InstrumentAddress.NormalizeAddress(),
			Data = SynFuturesExtensions.EncodePlaceCall(market.Expiry,
				signedSize, margin, tick, deadline),
			Value = BigInteger.Zero,
		};
	}

	public SynFuturesTransaction CreateCancelTransaction(
		SynFuturesMarket market, IReadOnlyList<int> ticks, uint deadline)
	{
		ValidateMarket(market);
		return new()
		{
			To = market.InstrumentAddress.NormalizeAddress(),
			Data = SynFuturesExtensions.EncodeCancelCall(market.Expiry, ticks,
				deadline),
			Value = BigInteger.Zero,
		};
	}

	public async ValueTask<SynFuturesRpcReceipt> SendAndWaitAsync(
		SynFuturesTransaction transaction, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var hash = await SendTransactionAsync(transaction, cancellationToken);
		var receipt = await WaitForReceiptAsync(hash, timeout,
			cancellationToken);
		EnsureSuccessful(receipt);
		return receipt;
	}

	public async ValueTask<string> SendTransactionAsync(
		SynFuturesTransaction transaction, CancellationToken cancellationToken)
	{
		EnsureSigningAvailable();
		ValidateTransaction(transaction);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var nonce = (await SendAsync<SynFuturesRpcAddressTagParameters,
				string>("eth_getTransactionCount", new()
				{
					Address = WalletAddress,
					BlockTag = "pending",
				}, true, cancellationToken)).ParseInteger();
			var estimated = await EstimateGasAsync(transaction,
				cancellationToken);
			if (estimated <= 0)
				throw new InvalidDataException(
					"SynFutures transaction gas estimate must be positive.");
			var gasLimit = (estimated * 120 + 99) / 100;
			var data = transaction.Data[2..].HexToByteArray();
			byte[] encoded;
			var fees = await TryGetEip1559FeesAsync(cancellationToken);
			if (fees is { } eip1559)
			{
				var tx = new Transaction1559(
					new BigInteger(SynFuturesExtensions.ChainId), nonce,
					eip1559.PriorityFee, eip1559.MaximumFee, gasLimit,
					transaction.To.NormalizeAddress(), transaction.Value,
					transaction.Data, null);
				new Transaction1559Signer().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			else
			{
				var gasPrice = (await SendAsync<SynFuturesRpcEmptyParameters,
					string>("eth_gasPrice", new(), true,
					cancellationToken)).ParseInteger();
				var tx = new LegacyTransactionChainId(
					nonce.ToBytesForRLPEncoding(),
					gasPrice.ToBytesForRLPEncoding(),
					gasLimit.ToBytesForRLPEncoding(),
					transaction.To.NormalizeAddress().HexToByteArray(),
					transaction.Value.ToBytesForRLPEncoding(), data,
					new BigInteger(SynFuturesExtensions.ChainId)
						.ToBytesForRLPEncoding());
				new LegacyTransactionSigner().SignTransaction(_privateKey, tx);
				encoded = tx.GetRLPEncoded();
			}
			return (await SendAsync<SynFuturesRpcValueParameters, string>(
				"eth_sendRawTransaction", new()
				{
					Value = encoded.ToHex(true),
				}, false, cancellationToken)).NormalizeHash();
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	public ValueTask<SynFuturesRpcReceipt> GetReceiptAsync(string hash,
		CancellationToken cancellationToken)
		=> SendAsync<SynFuturesRpcValueParameters, SynFuturesRpcReceipt>(
			"eth_getTransactionReceipt", new()
			{
				Value = hash.NormalizeHash(),
			}, true, cancellationToken);

	public async ValueTask<SynFuturesRpcReceipt> WaitForReceiptAsync(
		string hash, TimeSpan timeout, CancellationToken cancellationToken)
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
			"SynFutures transaction '" + hash +
			"' was not mined within " + timeout + ".");
	}

	public async ValueTask<DateTime> GetReceiptTimeAsync(
		SynFuturesRpcReceipt receipt, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		var block = await SendAsync<SynFuturesRpcTagBooleanParameters,
			SynFuturesRpcBlock>("eth_getBlockByNumber", new()
			{
				BlockTag = receipt.BlockNumber,
				IsTransactionsIncluded = false,
			}, true, cancellationToken) ?? throw new InvalidDataException(
				"Base returned no transaction block.");
		var seconds = block.Timestamp.ParseInteger();
		if (seconds <= 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				"Base returned an invalid block timestamp.");
		return ((long)seconds).ToUtc();
	}

	public SynFuturesPlaceEvent TryGetPlaceEvent(
		SynFuturesRpcReceipt receipt, SynFuturesMarket market)
	{
		var log = FindLog(receipt, market.InstrumentAddress,
			SynFuturesExtensions.PlaceTopic);
		if (log is null || log.Topics?.Length < 3)
			return null;
		return new()
		{
			Expiry = ToUInt32(log.Topics[1].ParseInteger(), "expiry"),
			Tick = ToInt32(SynFuturesExtensions.ReadAbiWord(log.Data, 0, true),
				"tick"),
			Nonce = ToUInt32(SynFuturesExtensions.ReadAbiWord(log.Data, 1),
				"nonce"),
			Balance = SynFuturesExtensions.ReadAbiWord(log.Data, 2),
			Size = SynFuturesExtensions.ReadAbiWord(log.Data, 3, true),
		};
	}

	public SynFuturesTradeEvent TryGetTradeEvent(
		SynFuturesRpcReceipt receipt, SynFuturesMarket market)
	{
		var log = FindLog(receipt, market.InstrumentAddress,
			SynFuturesExtensions.TradeTopic);
		if (log is null || log.Topics?.Length < 3)
			return null;
		return new()
		{
			Expiry = ToUInt32(log.Topics[1].ParseInteger(), "expiry"),
			Size = SynFuturesExtensions.ReadAbiWord(log.Data, 0, true),
			Amount = SynFuturesExtensions.ReadAbiWord(log.Data, 1),
			TakenSize = SynFuturesExtensions.ReadAbiWord(log.Data, 2, true),
			TakenValue = SynFuturesExtensions.ReadAbiWord(log.Data, 3),
			EntryNotional = SynFuturesExtensions.ReadAbiWord(log.Data, 4),
			Mark = SynFuturesExtensions.ReadAbiWord(log.Data, 7),
		};
	}

	public static decimal? GetCommission(SynFuturesRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		return (receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger()).FromBaseUnits(18);
	}

	private async ValueTask<BigInteger> EstimateGasAsync(
		SynFuturesTransaction transaction, CancellationToken cancellationToken)
		=> (await SendAsync<SynFuturesRpcCallOnlyParameters, string>(
			"eth_estimateGas", new()
			{
				Call = ToRpcCall(transaction),
			}, true, cancellationToken)).ParseInteger();

	private async ValueTask<(BigInteger PriorityFee, BigInteger MaximumFee)?>
		TryGetEip1559FeesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var block = await SendAsync<SynFuturesRpcTagBooleanParameters,
				SynFuturesRpcBlock>("eth_getBlockByNumber", new()
				{
					BlockTag = "latest",
					IsTransactionsIncluded = false,
				}, true, cancellationToken);
			if (block?.BaseFeePerGas.IsEmpty() != false)
				return null;
			var baseFee = block.BaseFeePerGas.ParseInteger();
			var priority = (await SendAsync<SynFuturesRpcEmptyParameters,
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

	private async ValueTask<TResult> SendAsync<TParameters, TResult>(
		string method, TParameters parameters, bool isRead,
		CancellationToken cancellationToken)
		where TParameters : SynFuturesRpcParameters
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(
			new SynFuturesRpcRequest<TParameters>
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
					"Base JSON-RPC HTTP " + (int)response.StatusCode + ": " +
					Truncate(body));
			SynFuturesRpcResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					SynFuturesRpcResponse<TResult>>(body, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Base JSON-RPC returned an unexpected response shape.", error);
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

	private SynFuturesRpcCall ToRpcCall(SynFuturesTransaction transaction)
		=> new()
		{
			From = WalletAddress,
			To = transaction.To.NormalizeAddress(),
			Data = transaction.Data,
			Value = transaction.Value.ToRpcHex(),
		};

	private static SynFuturesRpcLog FindLog(SynFuturesRpcReceipt receipt,
		string address, string topic)
		=> (receipt?.Logs ?? []).FirstOrDefault(log => log is not null &&
			!log.IsRemoved && log.Address?.Equals(address,
				StringComparison.OrdinalIgnoreCase) == true &&
			log.Topics?.FirstOrDefault()?.Equals(topic,
				StringComparison.OrdinalIgnoreCase) == true);

	private static int ToInt32(BigInteger value, string name)
	{
		if (value < int.MinValue || value > int.MaxValue)
			throw new InvalidDataException(
				"SynFutures returned an invalid " + name + " '" + value + "'.");
		return (int)value;
	}

	private static uint ToUInt32(BigInteger value, string name)
	{
		if (value < uint.MinValue || value > uint.MaxValue)
			throw new InvalidDataException(
				"SynFutures returned an invalid " + name + " '" + value + "'.");
		return (uint)value;
	}

	private static void EnsureSuccessful(SynFuturesRpcReceipt receipt)
	{
		if (receipt is null)
			throw new InvalidDataException(
				"SynFutures returned no transaction receipt.");
		if (receipt.Status.ParseInteger() == 0)
			throw new InvalidOperationException(
				"SynFutures transaction '" + receipt.TransactionHash +
				"' reverted.");
	}

	private void EnsureWalletConfigured()
	{
		if (!IsWalletConfigured)
			throw new InvalidOperationException(
				"A SynFutures wallet address is required for account data.");
	}

	private void EnsureSigningAvailable()
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for SynFutures transactions.");
	}

	private static void ValidateMarket(SynFuturesMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		_ = market.InstrumentAddress.NormalizeAddress();
		if (market.Expiry == 0)
			throw new InvalidDataException(
				"SynFutures market has an invalid expiry.");
	}

	private static void ValidateTransaction(SynFuturesTransaction transaction)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		_ = transaction.To.NormalizeAddress();
		if (transaction.Data.IsEmpty() ||
			!transaction.Data.StartsWith("0x",
				StringComparison.OrdinalIgnoreCase) ||
			transaction.Data.Length <= 2 || transaction.Data[2..].Any(
				static value => !Uri.IsHexDigit(value)) ||
			(transaction.Data.Length - 2) % 2 != 0)
			throw new InvalidDataException(
				"SynFutures transaction calldata must be valid hex.");
		if (transaction.Value < 0)
			throw new InvalidDataException(
				"SynFutures transaction value cannot be negative.");
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
