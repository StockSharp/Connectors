namespace StockSharp.Cetus.Native;

sealed class CetusSuiClient : BaseLogReceiver
{
	private const int _maximumPages = 20;

	private readonly GrpcChannel _channel;
	private readonly LedgerService.LedgerServiceClient _ledgerClient;
	private readonly StateService.StateServiceClient _stateClient;
	private readonly TransactionExecutionService.
		TransactionExecutionServiceClient _executionClient;
	private readonly SubscriptionService.SubscriptionServiceClient
		_subscriptionClient;
	private readonly CetusSigner _signer;
	private bool _isDisposed;

	public CetusSuiClient(string endpoint, string walletAddress,
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
		_subscriptionClient = new(_channel);
	}

	public override string Name => "Cetus_Sui_gRPC";
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

	public async ValueTask<CetusSharedObject> GetSharedObjectAsync(
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

	public async ValueTask<CetusToken> GetTokenAsync(string coinType,
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
				$"Cetus coin '{coinType}' uses {metadata.Decimals} decimals; " +
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

	public async ValueTask<CetusPreparedTransaction> PrepareSwapAsync(
		CetusMarket market, CetusQuote quote, ulong amountLimit,
		CetusSharedObject globalConfig, CetusSharedObject clock,
		CancellationToken cancellationToken)
	{
		EnsureSigning();
		var coins = await GetCoinObjectsAsync(quote.InputCoinType,
			cancellationToken);
		var transaction = CetusTransactionBuilder.BuildSwap(WalletAddress,
			market, quote, amountLimit, coins, globalConfig, clock);
		var request = new SimulateTransactionRequest
		{
			Transaction = transaction,
			Checks = SimulateTransactionRequest.Types.TransactionChecks.Enabled,
			DoGasSelection = true,
			ReadMask = CreateMask(
				"transaction.digest",
				"transaction.transaction.bcs",
				"transaction.effects.status",
				"transaction.effects.gas_used",
				"transaction.events.events.event_type",
				"transaction.events.events.contents"),
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
		var swap = FindSwapEvent(simulated, DateTime.UtcNow, market.PoolId) ??
			throw new InvalidDataException(
				"Cetus simulation returned no matching SwapEvent.");
		ValidateSwap(swap, quote, amountLimit, "simulation");
		return new()
		{
			Transaction = transactionBcs.Clone(),
			GasUsed = simulated.Effects?.GasUsed?.Clone(),
			Swap = swap,
		};
	}

	public async ValueTask<CetusTransactionReceipt> ExecuteSwapAsync(
		CetusPreparedTransaction prepared, CetusQuote quote, ulong amountLimit,
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
			var receipt = ReadReceipt(response?.Transaction, DateTime.UtcNow,
				quote.PoolId);
			if (receipt.IsSuccessful)
			{
				if (receipt.Swap is null)
					throw new InvalidDataException(
						"Successful Cetus execution returned no SwapEvent.");
				ValidateSwap(receipt.Swap, quote, amountLimit, "execution");
			}
			return receipt;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	public async ValueTask<CetusTransactionReceipt> GetReceiptAsync(
		string transactionDigest, string poolId,
		CancellationToken cancellationToken)
	{
		transactionDigest = transactionDigest.NormalizeTransactionDigest();
		poolId = poolId.NormalizeSuiAddress();
		var response = await CallAsync(token =>
			_ledgerClient.GetTransactionAsync(new()
			{
				Digest = transactionDigest,
				ReadMask = CreateExecutionMask(),
			}, cancellationToken: token), cancellationToken);
		return ReadReceipt(response?.Transaction, DateTime.UtcNow, poolId);
	}

	public CetusCheckpointClient CreateCheckpointClient(
		Func<CetusSwapEvent, ValueTask> swapHandler,
		Func<Exception, ValueTask> errorHandler)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		return new(_subscriptionClient, swapHandler, errorHandler)
		{
			Parent = this,
		};
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
			"timestamp",
			"events.events.event_type",
			"events.events.contents");

	private static FieldMask CreateMask(params string[] paths)
	{
		var result = new FieldMask();
		result.Paths.AddRange(paths);
		return result;
	}

	private static CetusTransactionReceipt ReadReceipt(
		ExecutedTransaction transaction, DateTime fallback, string poolId)
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
			Swap = FindSwapEvent(transaction, time, poolId),
		};
	}

	private static CetusSwapEvent FindSwapEvent(
		ExecutedTransaction transaction, DateTime fallback, string poolId)
	{
		poolId = poolId.NormalizeSuiAddress();
		var time = transaction.Timestamp.ToUtc(fallback);
		if (transaction.Events is null)
			return null;
		for (var index = 0; index < transaction.Events.Events.Count; index++)
		{
			var swap = transaction.Events.Events[index].ReadSwapEvent(
				transaction.Digest, index, time);
			if (swap is not null && swap.PoolId == poolId)
				return swap;
		}
		return null;
	}

	private static void ValidateExecutionStatus(ExecutionStatus status,
		string operation)
	{
		if (status is null)
			throw new InvalidDataException(
				$"Sui {operation} returned no execution status.");
		if (!status.Success)
			throw new InvalidOperationException(
				$"Cetus {operation} failed: " +
				$"{status.Error?.Description ?? "unknown Sui execution error"}");
	}

	private static void ValidateSwap(CetusSwapEvent swap, CetusQuote quote,
		ulong amountLimit, string operation)
	{
		if (swap.PoolId != quote.PoolId || swap.IsAToB != quote.IsAToB)
			throw new InvalidDataException(
				$"Cetus {operation} returned a different pool or direction.");
		if (quote.Kind == CetusSwapKinds.ExactInput)
		{
			if (swap.InputAmount != quote.InputAmount ||
				swap.OutputAmount < amountLimit)
				throw new InvalidDataException(
					$"Cetus {operation} violated the exact-input quote bounds.");
		}
		else if (quote.Kind == CetusSwapKinds.ExactOutput)
		{
			if (swap.OutputAmount != quote.OutputAmount ||
				swap.InputAmount > amountLimit)
				throw new InvalidDataException(
					$"Cetus {operation} violated the exact-output quote bounds.");
		}
		else
			throw new ArgumentOutOfRangeException(nameof(quote.Kind), quote.Kind,
				"Unsupported Cetus swap kind.");
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
				"A Sui Ed25519 private key is required for Cetus swaps.");
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
