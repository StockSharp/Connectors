namespace StockSharp.Raydium.Native;

sealed class RaydiumRpcClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private const int _maximumSerializedTransactionLength = 64 * 1024;
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

	public RaydiumRpcClient(string endpoint, RaydiumClusters cluster,
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
			"StockSharp-Raydium/1.0");

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
					"The configured Raydium wallet does not match the " +
					"private key.", nameof(walletAddress));
			}
		}
		WalletAddress = derivedAddress ?? walletAddress;
	}

	public string WalletAddress { get; }

	public override string Name => "Raydium_JSON-RPC";

	public bool IsWalletAvailable => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _account is not null;

	public async ValueTask VerifyAsync(CancellationToken cancellationToken)
	{
		var health = await SendAsync<string>("getHealth",
			new RaydiumRpcEmptyParameters(), cancellationToken);
		if (!health.EqualsIgnoreCase("ok"))
			throw new InvalidDataException(
				$"Solana RPC health check returned '{health}'.");
	}

	public async ValueTask VerifyProgramsAsync(
		IEnumerable<string> programAddresses,
		CancellationToken cancellationToken)
	{
		var programs = (programAddresses ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		if (programs.Length == 0)
			throw new ArgumentException(
				"At least one Raydium program is required.",
				nameof(programAddresses));
		for (var offset = 0; offset < programs.Length; offset += 100)
		{
			var chunk = programs.Skip(offset).Take(100).ToArray();
			var accounts = await GetAccountsAsync(chunk, cancellationToken);
			for (var index = 0; index < chunk.Length; index++)
				if (index >= accounts.Length || accounts[index] is null ||
					!accounts[index].IsExecutable)
					throw new InvalidDataException(
						$"The Solana RPC endpoint does not expose executable " +
						$"Raydium program '{chunk[index]}'.");
		}
	}

	public async ValueTask<RaydiumRpcAccount> GetAccountAsync(string address,
		CancellationToken cancellationToken)
		=> (await SendAsync<RaydiumRpcContextValue<RaydiumRpcAccount>>(
			"getAccountInfo", new RaydiumRpcAddressAccountParameters
			{
				Address = address.NormalizePublicKey(),
				Config = new(),
			}, cancellationToken)).Value;

	public async ValueTask<RaydiumRpcAccount[]> GetAccountsAsync(
		IEnumerable<string> addresses, CancellationToken cancellationToken)
	{
		var normalized = (addresses ?? []).Select(static address =>
			address.NormalizePublicKey()).ToArray();
		if (normalized.Length is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(addresses),
				"Solana getMultipleAccounts requires between 1 and 100 addresses.");
		return (await SendAsync<RaydiumRpcContextValue<RaydiumRpcAccount[]>>(
			"getMultipleAccounts", new RaydiumRpcAddressesAccountParameters
			{
				Addresses = normalized,
				Config = new(),
			}, cancellationToken)).Value ?? [];
	}

	public async ValueTask<ulong> GetBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWallet();
		return (await SendAsync<RaydiumRpcContextValue<ulong>>("getBalance",
			new RaydiumRpcAddressCommitmentParameters
			{
				Address = WalletAddress,
				Config = new(),
			}, cancellationToken)).Value;
	}

	public ValueTask<RaydiumRpcSignatureInfo[]> GetSignaturesAsync(
		string address, string before, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return SendAsync<RaydiumRpcSignatureInfo[]>("getSignaturesForAddress",
			new RaydiumRpcSignaturesParameters
			{
				Address = address.NormalizePublicKey(),
				Config = new()
				{
					Before = before,
					Limit = limit,
				},
			}, cancellationToken);
	}

	public ValueTask<RaydiumRpcTransaction> GetTransactionAsync(
		string signature, CancellationToken cancellationToken)
		=> SendAsync<RaydiumRpcTransaction>("getTransaction",
			new RaydiumRpcTransactionParameters
			{
				Signature = NormalizeSignature(signature),
				Config = new(),
			}, cancellationToken);

	public async ValueTask<string> SendSerializedTransactionAsync(
		string encoded, CancellationToken cancellationToken)
	{
		EnsureSigning();
		encoded = encoded.ThrowIfEmpty(nameof(encoded)).Trim();
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(encoded);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Raydium returned an invalid base64 transaction.", error);
		}
		if (bytes.Length is < 1 or > _maximumSerializedTransactionLength)
			throw new InvalidDataException(
				"Raydium returned a transaction with an invalid size.");
		Transaction transaction;
		try
		{
			transaction = Transaction.Deserialize(bytes);
		}
		catch (Exception error) when (error is ArgumentException or
			InvalidDataException or IndexOutOfRangeException)
		{
			throw new InvalidDataException(
				"Raydium returned an invalid serialized Solana transaction.", error);
		}
		if (transaction.FeePayer?.Key != WalletAddress)
			throw new InvalidDataException(
				"Raydium transaction fee payer does not match the wallet.");
		if (transaction.Instructions is not { Count: > 0 })
			throw new InvalidDataException(
				"Raydium transaction contains no instructions.");
		var requiredSigners = transaction.Instructions
			.SelectMany(static instruction => instruction.Keys ?? [])
			.Where(static key => key.IsSigner)
			.Select(static key => key.PublicKey)
			.Concat((transaction.Signatures ?? []).Select(static pair =>
				pair.PublicKey.Key))
			.Append(transaction.FeePayer.Key)
			.Distinct(StringComparer.Ordinal).ToArray();
		if (requiredSigners.Length != 1 ||
			!requiredSigners[0].Equals(WalletAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Raydium transaction requires an unexpected signer.");
		transaction.Signatures ??= [];
		transaction.Signatures.Clear();
		if (!transaction.Sign(_account))
			throw new CryptographicException(
				"The Raydium transaction signature could not be verified.");
		return NormalizeSignature(await SendAsync<string>("sendTransaction",
			new RaydiumRpcSendTransactionParameters
			{
				Transaction = Convert.ToBase64String(transaction.Serialize()),
				Config = new()
				{
					MaximumRetries = 3,
				},
			}, cancellationToken));
	}

	public async ValueTask<RaydiumTransactionReceipt> GetReceiptAsync(
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
			Transaction = transaction,
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
		RaydiumRpcParameters parameters, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			var request = new RaydiumRpcRequest<RaydiumRpcParameters>
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
				throw new RaydiumApiException(response.StatusCode,
					$"Solana RPC '{method}' failed: {Limit(body, 1024)}");
			RaydiumRpcResponse<TResult> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					RaydiumRpcResponse<TResult>>(body, _serializerSettings);
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
				"A Solana private key is required to sign Raydium swaps.");
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
