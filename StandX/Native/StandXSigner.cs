namespace StockSharp.StandX.Native;

sealed class StandXSigner : Disposable
{
	private readonly StandXChains _chain;
	private readonly EthECKey _evmKey;
	private readonly Account _solanaWallet;
	private readonly Account _requestSigner;
	private readonly JsonSerializerSettings _jsonSettings;

	public StandXSigner(StandXChains chain, string walletAddress,
		SecureString privateKey, JsonSerializerSettings jsonSettings)
	{
		if (!Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported StandX wallet chain.");
		if (privateKey.IsEmpty())
			throw new ArgumentNullException(nameof(privateKey));
		_chain = chain;
		_jsonSettings = jsonSettings ?? throw new ArgumentNullException(
			nameof(jsonSettings));
		var secret = privateKey.UnSecure().Trim();
		if (chain == StandXChains.Bsc)
		{
			try
			{
				_evmKey = new(secret);
			}
			catch (Exception error)
			{
				throw new ArgumentException("Invalid StandX BSC private key.",
					nameof(privateKey), error);
			}
			Address = _evmKey.GetPublicAddress().ToLowerInvariant();
			if (!walletAddress.IsEmpty() &&
				!AddressUtil.Current.AreAddressesTheSame(walletAddress.Trim(),
					Address))
				throw new ArgumentException(
					"The StandX BSC wallet address does not match the private key.",
					nameof(walletAddress));
		}
		else
		{
			byte[] bytes = null;
			byte[] publicKey = null;
			try
			{
				bytes = Encoders.Base58.DecodeData(secret);
				if (bytes.Length != 64)
					throw new ArgumentException(
						"StandX Solana private key must be a base58-encoded " +
						"64-byte keypair.", nameof(privateKey));
				publicKey = bytes.AsSpan(32, 32).ToArray();
				_solanaWallet = new(bytes, publicKey);
				Address = _solanaWallet.PublicKey.Key;
				if (!walletAddress.IsEmpty() &&
					!Address.Equals(walletAddress.Trim(),
						StringComparison.Ordinal))
					throw new ArgumentException(
						"The StandX Solana wallet address does not match the " +
						"private key.", nameof(walletAddress));
			}
			catch (ArgumentException)
			{
				throw;
			}
			catch (Exception error)
			{
				throw new ArgumentException("Invalid StandX Solana private key.",
					nameof(privateKey), error);
			}
			finally
			{
				if (bytes is not null)
					CryptographicOperations.ZeroMemory(bytes);
				if (publicKey is not null)
					CryptographicOperations.ZeroMemory(publicKey);
			}
		}

		_requestSigner = new();
		RequestId = _requestSigner.PublicKey.Key;
	}

	public StandXChains Chain => _chain;

	public string Address { get; }

	public string RequestId { get; }

	public void ValidateSignedData(StandXSignedData payload)
	{
		ArgumentNullException.ThrowIfNull(payload);
		if (!payload.Domain.EqualsIgnoreCase("standx.com") ||
			!payload.Uri.EqualsIgnoreCase("https://standx.com"))
			throw new CryptographicException(
				"StandX sign-in challenge has an unexpected domain or URI.");
		if (!payload.RequestId.Equals(RequestId, StringComparison.Ordinal))
			throw new CryptographicException(
				"StandX sign-in challenge does not contain this session key.");
		var addressMatches = _chain == StandXChains.Bsc
			? AddressUtil.Current.AreAddressesTheSame(payload.Address, Address)
			: payload.Address.Equals(Address, StringComparison.Ordinal);
		if (!addressMatches)
			throw new CryptographicException(
				"StandX sign-in challenge contains a different wallet address.");
		if (_chain == StandXChains.Bsc && payload.ChainId is not 56)
			throw new CryptographicException(
				"StandX BSC sign-in challenge has an unexpected chain ID.");
	}

	public string SignLogin(StandXSignedData payload)
	{
		ValidateSignedData(payload);
		var message = payload.Message.ThrowIfEmpty(nameof(payload.Message));
		if (_chain == StandXChains.Bsc)
			return new EthereumMessageSigner().EncodeUTF8AndSign(message, _evmKey);

		var messageBytes = Encoding.UTF8.GetBytes(message);
		var signature = _solanaWallet.Sign(messageBytes);
		try
		{
			var envelope = new StandXSolanaLoginEnvelope
			{
				Input = payload,
				Output = new()
				{
					SignedMessage = messageBytes.Select(static value =>
						(int)value).ToArray(),
					Signature = signature.Select(static value =>
						(int)value).ToArray(),
					Account = new()
					{
						PublicKey = _solanaWallet.PublicKey.KeyBytes
							.Select(static value => (int)value).ToArray(),
					},
				},
			};
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(
				JsonConvert.SerializeObject(envelope, _jsonSettings)));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(messageBytes);
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	public StandXRequestSignature SignRequest(string payload, string requestId,
		long timestamp)
	{
		payload ??= string.Empty;
		requestId = requestId.ThrowIfEmpty(nameof(requestId));
		if (timestamp <= 0)
			throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp,
				"StandX request timestamp must be positive.");
		const string version = "v1";
		var bytes = Encoding.UTF8.GetBytes($"{version},{requestId},{timestamp}," +
			payload);
		var signature = _requestSigner.Sign(bytes);
		try
		{
			return new()
			{
				Version = version,
				RequestId = requestId,
				Timestamp = timestamp.ToString(CultureInfo.InvariantCulture),
				Signature = Convert.ToBase64String(signature),
			};
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	protected override void DisposeManaged()
	{
		if (_solanaWallet?.PrivateKey.KeyBytes is { } walletKey)
			CryptographicOperations.ZeroMemory(walletKey);
		if (_requestSigner?.PrivateKey.KeyBytes is { } requestKey)
			CryptographicOperations.ZeroMemory(requestKey);
		base.DisposeManaged();
	}
}
