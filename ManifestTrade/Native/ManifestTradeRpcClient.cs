namespace StockSharp.ManifestTrade.Native;

sealed class ManifestTradeRpcClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _httpClient;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private readonly Account _account;
	private long _requestId;
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public ManifestTradeRpcClient(string endpoint, ManifestTradeClusters cluster,
		string walletAddress, SecureString privateKey)
	{
		endpoint = NormalizeEndpoint(endpoint.IsEmpty()
			? cluster.GetRpcEndpoint()
			: endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint, UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-ManifestTrade/1.0");

		if (!privateKey.IsEmpty())
		{
			byte[] secret = null;
			byte[] publicKey = null;
			try
			{
				secret = Encoders.Base58.DecodeData(privateKey.UnSecure().Trim());
				if (secret.Length != PrivateKey.PrivateKeyLength)
					throw new ArgumentException(
						"The Solana private key must be a base58-encoded " +
						"64-byte keypair.", nameof(privateKey));
				publicKey = secret.AsSpan(32, PublicKey.PublicKeyLength).ToArray();
				_account = new Account(secret, publicKey);
			}
			finally
			{
				if (secret is not null)
					CryptographicOperations.ZeroMemory(secret);
				if (publicKey is not null)
					CryptographicOperations.ZeroMemory(publicKey);
			}
		}

		var derivedAddress = _account?.PublicKey.Key;
		if (!walletAddress.IsEmpty())
		{
			walletAddress = walletAddress.NormalizePublicKey();
			if (!derivedAddress.IsEmpty() &&
				!walletAddress.Equals(derivedAddress, StringComparison.Ordinal))
			{
				CryptographicOperations.ZeroMemory(_account.PrivateKey.KeyBytes);
				throw new ArgumentException(
					"The configured ManifestTrade wallet does not match the private key.",
					nameof(walletAddress));
			}
		}
		WalletAddress = derivedAddress ?? walletAddress;
	}

	public string WalletAddress { get; }

	public override string Name => "ManifestTrade_JSON-RPC";

	public bool IsWalletAvailable => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _account is not null;

	public async ValueTask VerifyAsync(CancellationToken cancellationToken)
	{
		var health = await SendAsync<string>("getHealth",
			new ManifestTradeRpcEmptyParameters(), cancellationToken);
		if (!health.EqualsIgnoreCase("ok"))
			throw new InvalidDataException(
				$"Solana RPC health check returned '{health}'.");
		var program = await GetAccountAsync(ManifestTradeExtensions.ProgramAddress,
			cancellationToken);
		if (program is null || !program.IsExecutable ||
			!program.Owner.Equals(
				"BPFLoaderUpgradeab1e11111111111111111111111",
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"The configured Solana RPC endpoint does not expose the " +
				"Manifest Trade program.");
	}

	public ValueTask<long> GetSlotAsync(CancellationToken cancellationToken)
		=> SendAsync<long>("getSlot", new ManifestTradeRpcEmptyParameters(),
			cancellationToken);

	public async ValueTask<ManifestTradeRpcAccount> GetAccountAsync(string address,
		CancellationToken cancellationToken)
		=> (await SendAsync<ManifestTradeRpcContextValue<ManifestTradeRpcAccount>>(
			"getAccountInfo", new ManifestTradeRpcAddressAccountParameters
			{
				Address = address.NormalizePublicKey(),
				Config = new(),
			}, cancellationToken)).Value;

	public async ValueTask<ManifestTradeRpcAccount[]> GetAccountsAsync(
		IEnumerable<string> addresses, CancellationToken cancellationToken)
	{
		var normalized = (addresses ?? []).Select(static address =>
			address.NormalizePublicKey()).ToArray();
		if (normalized.Length is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(addresses),
				"Solana getMultipleAccounts requires between 1 and 100 addresses.");
		return (await SendAsync<ManifestTradeRpcContextValue<ManifestTradeRpcAccount[]>>(
			"getMultipleAccounts", new ManifestTradeRpcAddressesAccountParameters
			{
				Addresses = normalized,
				Config = new(),
			}, cancellationToken)).Value ?? [];
	}

	public async ValueTask<ulong> GetBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWallet();
		return (await SendAsync<ManifestTradeRpcContextValue<ulong>>("getBalance",
			new ManifestTradeRpcAddressCommitmentParameters
			{
				Address = WalletAddress,
				Config = new(),
			}, cancellationToken)).Value;
	}

	public ValueTask<ManifestTradeRpcSignatureInfo[]> GetSignaturesAsync(
		string address, string before, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return SendAsync<ManifestTradeRpcSignatureInfo[]>("getSignaturesForAddress",
			new ManifestTradeRpcSignaturesParameters
			{
				Address = address.NormalizePublicKey(),
				Config = new()
				{
					Before = before,
					Limit = limit,
				},
			}, cancellationToken);
	}

	public ValueTask<ManifestTradeRpcTransaction> GetTransactionAsync(
		string signature, CancellationToken cancellationToken)
		=> SendAsync<ManifestTradeRpcTransaction>("getTransaction",
			new ManifestTradeRpcTransactionParameters
			{
				Signature = NormalizeSignature(signature),
				Config = new(),
			}, cancellationToken);

	public async ValueTask<ulong> GetPriorityFeeAsync(
		IEnumerable<string> writableAccounts,
		CancellationToken cancellationToken)
	{
		var accounts = (writableAccounts ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.Take(128).ToArray();
		var fees = await SendAsync<ManifestTradeRpcRecentFee[]>(
			"getRecentPrioritizationFees", new ManifestTradeRpcRecentFeesParameters
			{
				Addresses = accounts,
			}, cancellationToken) ?? [];
		var values = fees.Select(static fee => fee.PrioritizationFee)
			.Where(static fee => fee > 0).OrderBy(static fee => fee).ToArray();
		return values.Length == 0 ? 0 : values[values.Length / 2];
	}

	public async ValueTask<string> SendTransactionAsync(
		IEnumerable<TransactionInstruction> instructions,
		CancellationToken cancellationToken)
	{
		EnsureSigning();
		var list = (instructions ?? []).ToArray();
		if (list.Length == 0)
			throw new ArgumentException(
				"At least one Solana instruction is required.",
				nameof(instructions));
		var latest = await SendAsync<
			ManifestTradeRpcContextValue<ManifestTradeRpcLatestBlockhash>>(
			"getLatestBlockhash", new ManifestTradeRpcLatestBlockhashParameters
			{
				Config = new(),
			}, cancellationToken);
		if (latest?.Value?.Blockhash.IsEmpty() != false)
			throw new InvalidDataException(
				"Solana RPC returned an empty recent blockhash.");
		var builder = new TransactionBuilder()
			.SetFeePayer(_account.PublicKey)
			.SetRecentBlockHash(latest.Value.Blockhash);
		foreach (var instruction in list)
			builder.AddInstruction(instruction);
		var transaction = builder.Build(_account);
		return await SendAsync<string>("sendTransaction",
			new ManifestTradeRpcSendTransactionParameters
			{
				Transaction = Convert.ToBase64String(transaction),
				Config = new()
				{
					MaximumRetries = 3,
				},
			}, cancellationToken);
	}

	public async ValueTask<ManifestTradeTransactionReceipt> GetReceiptAsync(
		string signature, CancellationToken cancellationToken)
	{
		var transaction = await GetTransactionAsync(signature,
			cancellationToken);
		if (transaction is null)
			return null;
		return new()
		{
			Signature = NormalizeSignature(signature),
			IsSuccessful = transaction.Meta?.Error is null,
			Fee = transaction.Meta?.Fee ?? 0,
			Slot = transaction.Slot,
			BlockTime = transaction.BlockTime?.FromUnix(),
			LogMessages = transaction.Meta?.LogMessages ?? [],
		};
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		_requestGate.Dispose();
		if (_account?.PrivateKey.KeyBytes is { } key)
			CryptographicOperations.ZeroMemory(key);
		base.DisposeManaged();
	}

	private async ValueTask<TResult> SendAsync<TResult>(string method,
		ManifestTradeRpcParameters parameters, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			var request = new ManifestTradeRpcRequest<ManifestTradeRpcParameters>
			{
				Id = Interlocked.Increment(ref _requestId),
				Method = method,
				Parameters = parameters,
			};
			using var content = new StringContent(
				JsonConvert.SerializeObject(request, _serializerSettings),
				Encoding.UTF8, "application/json");
			using var response = await _httpClient.PostAsync(string.Empty,
				content, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
					HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			if (response.Content.Headers.ContentLength is long length &&
				length > _maximumResponseLength)
				throw new InvalidDataException(
					$"Solana RPC response for '{method}' exceeds the safety limit.");
			var body = await ReadBodyAsync(response.Content, method,
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new ManifestTradeRpcException(response.StatusCode,
					$"Solana RPC '{method}' failed: {Limit(body, 1024)}");
			ManifestTradeRpcResponse<TResult> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					ManifestTradeRpcResponse<TResult>>(body, _serializerSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Solana RPC returned malformed JSON for '{method}'.",
					error);
			}
			if (envelope is null)
				throw new InvalidDataException(
					$"Solana RPC returned an empty response for '{method}'.");
			if (envelope.Error is not null)
				throw new InvalidOperationException(
					$"Solana RPC '{method}' failed ({envelope.Error.Code}): " +
					$"{envelope.Error.Message}");
			return envelope.Result;
		}
	}

	private async ValueTask WaitForRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private void EnsureWallet()
	{
		if (!IsWalletAvailable)
			throw new InvalidOperationException(
				"A Solana wallet address is required for this operation.");
	}

	private void EnsureSigning()
	{
		EnsureWallet();
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Solana private key is required to sign Manifest Trade " +
				"transactions.");
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Solana RPC endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string NormalizeSignature(string signature)
	{
		signature = signature.ThrowIfEmpty(nameof(signature)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(signature);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new ArgumentException(
				"Invalid base58 Solana transaction signature.",
				nameof(signature), error);
		}
		if (bytes.Length != 64)
			throw new ArgumentException(
				"A Solana transaction signature must contain 64 bytes.",
				nameof(signature));
		return signature;
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		string method, CancellationToken cancellationToken)
	{
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new InvalidDataException(
					$"Solana RPC response for '{method}' exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
