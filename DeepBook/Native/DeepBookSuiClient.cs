namespace StockSharp.DeepBook.Native;

sealed class DeepBookSuiClient : BaseLogReceiver
{
	private const int _maximumPages = 20;

	private readonly GrpcChannel _channel;
	private readonly LedgerService.LedgerServiceClient _ledgerClient;
	private readonly StateService.StateServiceClient _stateClient;
	private readonly TransactionExecutionService.
		TransactionExecutionServiceClient _executionClient;
	private readonly DeepBookSigner _signer;
	private bool _isDisposed;

	public DeepBookSuiClient(string endpoint, string walletAddress,
		SecureString privateKey)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_signer = new(walletAddress, privateKey);
		_channel = GrpcChannel.ForAddress(endpoint, new()
		{
			MaxReceiveMessageSize = 64 * 1024 * 1024,
			MaxSendMessageSize = 8 * 1024 * 1024,
		});
		_ledgerClient = new(_channel);
		_stateClient = new(_channel);
		_executionClient = new(_channel);
	}

	public override string Name => "DeepBook_Sui_gRPC";
	public string WalletAddress => _signer.WalletAddress;
	public bool IsWalletAvailable => _signer.IsWalletAvailable;
	public bool IsSigningAvailable => _signer.IsSigningAvailable;

	public ValueTask<GetServiceInfoResponse> GetServiceInfoAsync(
		CancellationToken cancellationToken)
		=> CallAsync(token => _ledgerClient.GetServiceInfoAsync(
			new GetServiceInfoRequest(), cancellationToken: token),
			cancellationToken);

	public async ValueTask<SuiObject> GetObjectAsync(string objectId,
		CancellationToken cancellationToken)
	{
		objectId = objectId.NormalizeSuiAddress();
		var response = await CallAsync(token => _ledgerClient.GetObjectAsync(
			new()
			{
				ObjectId = objectId,
				ReadMask = CreateMask("object_id", "version", "digest",
					"owner", "object_type"),
			}, cancellationToken: token), cancellationToken);
		var item = response?.Object ?? throw new InvalidDataException(
			$"Sui gRPC returned no object '{objectId}'.");
		if (item.ObjectId.NormalizeSuiAddress() != objectId ||
			item.Version == 0 || item.Digest.IsEmpty())
			throw new InvalidDataException(
				$"Sui gRPC returned incomplete object '{objectId}'.");
		return item;
	}

	public async ValueTask<DeepBookSharedObject> GetSharedObjectAsync(
		string objectId, bool isMutable, CancellationToken cancellationToken)
	{
		var item = await GetObjectAsync(objectId, cancellationToken);
		if (item.Owner?.Kind != Owner.Types.OwnerKind.Shared ||
			!item.Owner.HasVersion || item.Owner.Version == 0)
			throw new InvalidDataException(
				$"Sui object '{item.ObjectId}' is not a shared object.");
		return new()
		{
			ObjectId = item.ObjectId.NormalizeSuiAddress(),
			InitialVersion = item.Owner.Version,
			IsMutable = isMutable,
		};
	}

	public async ValueTask<DeepBookToken> GetTokenAsync(string coinType,
		CancellationToken cancellationToken)
	{
		coinType = coinType.NormalizeCoinType();
		var response = await CallAsync(token => _stateClient.GetCoinInfoAsync(
			new() { CoinType = coinType }, cancellationToken: token),
			cancellationToken);
		if (response is null || response.CoinType.IsEmpty() ||
			response.CoinType.NormalizeCoinType() != coinType ||
			response.Metadata is null)
			throw new InvalidDataException(
				$"Sui gRPC returned no metadata for '{coinType}'.");
		var metadata = response.Metadata;
		if (metadata.Decimals > 28)
			throw new NotSupportedException(
				$"DeepBook coin '{coinType}' uses {metadata.Decimals} decimals; " +
				"StockSharp decimal amounts support at most 28.");
		var symbol = metadata.Symbol.NormalizeTokenSymbol(coinType);
		return new()
		{
			CoinType = coinType,
			Symbol = symbol,
			Name = metadata.Name.NormalizeTokenName(symbol),
			Decimals = checked((int)metadata.Decimals),
		};
	}

	public async ValueTask<SuiObject[]> GetCoinObjectsAsync(string coinType,
		CancellationToken cancellationToken)
	{
		EnsureWallet();
		coinType = coinType.NormalizeCoinType();
		var objectType = ("0x2::coin::Coin<" + coinType + ">")
			.NormalizeCoinType();
		var result = new List<SuiObject>();
		var pageToken = ByteString.Empty;

		for (var page = 0; page < _maximumPages; page++)
		{
			var request = new ListOwnedObjectsRequest
			{
				Owner = WalletAddress,
				PageSize = 1000,
				ObjectType = objectType,
				ReadMask = CreateMask("object_id", "version", "digest",
					"object_type", "balance"),
			};
			if (pageToken.Length > 0)
				request.PageToken = pageToken;
			var response = await CallAsync(token =>
				_stateClient.ListOwnedObjectsAsync(request,
					cancellationToken: token), cancellationToken);
			if (response is null)
				throw new InvalidDataException(
					"Sui gRPC returned no owned-object page.");

			foreach (var item in response.Objects)
			{
				if (item.ObjectId.IsEmpty() || item.Version == 0 ||
					item.Digest.IsEmpty() || item.ObjectType.IsEmpty() ||
					!item.HasBalance)
					throw new InvalidDataException(
						"Sui gRPC returned an incomplete coin object.");
				if (item.ObjectType.NormalizeCoinType() != objectType)
					throw new InvalidDataException(
						"Sui gRPC returned a coin object of another type.");
				result.Add(item);
			}

			pageToken = response.NextPageToken;
			if (pageToken.Length == 0)
				return [.. result];
		}

		throw new InvalidDataException(
			"Sui owned-object pagination exceeded the safety limit.");
	}

	public async ValueTask<Balance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		EnsureWallet();
		var result = new List<Balance>();
		var pageToken = ByteString.Empty;

		for (var page = 0; page < _maximumPages; page++)
		{
			var request = new ListBalancesRequest
			{
				Owner = WalletAddress,
				PageSize = 1000,
			};
			if (pageToken.Length > 0)
				request.PageToken = pageToken;
			var response = await CallAsync(token =>
				_stateClient.ListBalancesAsync(request,
					cancellationToken: token), cancellationToken);
			if (response is null)
				throw new InvalidDataException(
					"Sui gRPC returned no balance page.");

			foreach (var balance in response.Balances)
			{
				if (balance.CoinType.IsEmpty() || !balance.HasBalance_)
					throw new InvalidDataException(
						"Sui gRPC returned an incomplete balance.");
				_ = balance.CoinType.NormalizeCoinType();
				result.Add(balance);
			}

			pageToken = response.NextPageToken;
			if (pageToken.Length == 0)
				return [.. result];
		}

		throw new InvalidDataException(
			"Sui balance pagination exceeded the safety limit.");
	}

	public async ValueTask<DeepBookPreparedTransaction> PrepareSwapAsync(
		string packageId, DeepBookMarket market, DeepBookQuote quote,
		ulong inputAmount, ulong minimumOutput, DeepBookSharedObject pool,
		DeepBookSharedObject clock,
		CancellationToken cancellationToken)
	{
		EnsureSigning();
		var coinType = quote.Side == Sides.Sell
			? market.BaseToken.CoinType
			: market.QuoteToken.CoinType;
		var coins = await GetCoinObjectsAsync(coinType,
			cancellationToken);
		var transaction = DeepBookTransactionBuilder.BuildSwap(WalletAddress,
			packageId, market, quote, inputAmount, minimumOutput, coins, pool,
			clock);
		var request = new SimulateTransactionRequest
		{
			Transaction = transaction,
			Checks = SimulateTransactionRequest.Types.TransactionChecks.Enabled,
			DoGasSelection = true,
			ReadMask = CreateMask(
				"transaction.digest",
				"transaction.transaction.bcs",
				"transaction.effects.status",
				"transaction.effects.gas_used"),
		};
		var response = await CallAsync(token =>
			_executionClient.SimulateTransactionAsync(request,
				cancellationToken: token), cancellationToken);
		var simulated = response?.Transaction ?? throw new InvalidDataException(
			"Sui simulation returned no transaction.");
		ValidateExecutionStatus(simulated.Effects?.Status, "simulation");
		var transactionBcs = simulated.Transaction?.Bcs;
		if (transactionBcs?.Value is not { Length: > 0 })
			throw new InvalidDataException(
				"Sui simulation returned no gas-selected transaction BCS.");
		return new()
		{
			Transaction = transactionBcs.Clone(),
			GasUsed = simulated.Effects?.GasUsed?.Clone(),
		};
	}

	public async ValueTask<DeepBookTransactionReceipt> ExecuteSwapAsync(
		DeepBookPreparedTransaction prepared,
		CancellationToken cancellationToken)
	{
		EnsureSigning();
		ArgumentNullException.ThrowIfNull(prepared);
		if (prepared.Transaction?.Value is not { Length: > 0 })
			throw new ArgumentException(
				"A prepared Sui transaction is required.", nameof(prepared));
		var transactionBytes = prepared.Transaction.Value.ToByteArray();
		var signature = _signer.SignTransaction(transactionBytes);
		try
		{
			var request = new ExecuteTransactionRequest
			{
				Transaction = new()
				{
					Bcs = prepared.Transaction.Clone(),
				},
				ReadMask = CreateExecutionMask(),
			};
			request.Signatures.Add(new UserSignature
			{
				Scheme = SignatureScheme.Ed25519,
				Bcs = new()
				{
					Name = "UserSignature",
					Value = ByteString.CopyFrom(signature),
				},
			});
			var response = await CallAsync(token =>
				_executionClient.ExecuteTransactionAsync(request,
					cancellationToken: token), cancellationToken);
			return ReadReceipt(response?.Transaction, DateTime.UtcNow);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	public async ValueTask<DeepBookTransactionReceipt> GetReceiptAsync(
		string transactionDigest,
		CancellationToken cancellationToken)
	{
		transactionDigest = transactionDigest.NormalizeTransactionDigest();
		var response = await CallAsync(token =>
			_ledgerClient.GetTransactionAsync(new()
			{
				Digest = transactionDigest,
				ReadMask = CreateExecutionMask(),
			}, cancellationToken: token), cancellationToken);
		return ReadReceipt(response?.Transaction, DateTime.UtcNow);
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_signer.Dispose();
		_channel.Dispose();
		base.DisposeManaged();
	}

	private static FieldMask CreateExecutionMask()
		=> CreateMask(
			"digest",
			"effects.status",
			"effects.gas_used",
			"checkpoint",
			"timestamp");

	private static FieldMask CreateMask(params string[] paths)
	{
		var result = new FieldMask();
		result.Paths.AddRange(paths);
		return result;
	}

	private static DeepBookTransactionReceipt ReadReceipt(
		ExecutedTransaction transaction, DateTime fallback)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		var digest = transaction.Digest.NormalizeTransactionDigest();
		var status = transaction.Effects?.Status ?? throw new
			InvalidDataException(
				$"Sui transaction '{digest}' returned no execution status.");
		var time = transaction.Timestamp.ToUtc(fallback);
		return new()
		{
			TransactionDigest = digest,
			IsSuccessful = status.Success,
			Error = status.Success
				? null
				: status.Error?.Description ?? "Sui transaction failed.",
			Time = time,
			Checkpoint = transaction.HasCheckpoint
				? transaction.Checkpoint
				: null,
			GasUsed = transaction.Effects?.GasUsed?.Clone(),
		};
	}

	private static void ValidateExecutionStatus(ExecutionStatus status,
		string operation)
	{
		if (status is null)
			throw new InvalidDataException(
				$"Sui {operation} returned no execution status.");
		if (!status.Success)
			throw new InvalidOperationException(
				$"DeepBook {operation} failed: " +
				$"{status.Error?.Description ?? "unknown Sui execution error"}");
	}


	private async ValueTask<TResult> CallAsync<TResult>(
		Func<CancellationToken, AsyncUnaryCall<TResult>> action,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		for (var attempt = 0; ; attempt++)
		{
			try
			{
				using var call = action(cancellationToken);
				return await call.ResponseAsync.WaitAsync(cancellationToken);
			}
			catch (RpcException error) when (attempt < 2 &&
				IsTransient(error.StatusCode))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
			}
		}
	}

	private void EnsureWallet()
	{
		if (!IsWalletAvailable)
			throw new InvalidOperationException(
				"A Sui wallet address is required for this operation.");
	}

	private void EnsureSigning()
	{
		EnsureWallet();
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Sui Ed25519 private key is required for DeepBook swaps.");
	}

	private static bool IsTransient(StatusCode statusCode)
		=> statusCode is StatusCode.Unavailable or
			StatusCode.ResourceExhausted or StatusCode.DeadlineExceeded;

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Sui gRPC endpoint must use HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}
}
