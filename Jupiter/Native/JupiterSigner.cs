namespace StockSharp.Jupiter.Native;

sealed class JupiterSigner : Disposable
{
	private const int _maximumTransactionLength = 64 * 1024;
	private readonly Account _account;

	public JupiterSigner(string walletAddress, SecureString privateKey)
	{
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
				publicKey = secret.AsSpan(32,
					PublicKey.PublicKeyLength).ToArray();
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
				!walletAddress.Equals(derivedAddress,
					StringComparison.Ordinal))
			{
				CryptographicOperations.ZeroMemory(
					_account.PrivateKey.KeyBytes);
				throw new ArgumentException(
					"The configured Jupiter wallet does not match the " +
					"private key.", nameof(walletAddress));
			}
		}
		WalletAddress = derivedAddress ?? walletAddress;
	}

	public string WalletAddress { get; }

	public bool IsWalletAvailable => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _account is not null;

	public string SignTransaction(string encoded)
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Solana private key is required for Jupiter trading.");
		encoded = encoded.ThrowIfEmpty(nameof(encoded)).Trim();
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(encoded);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Jupiter returned an invalid base64 transaction.", error);
		}
		if (bytes.Length is < 1 or > _maximumTransactionLength)
			throw new InvalidDataException(
				"Jupiter returned a transaction with an invalid size.");

		Transaction transaction;
		try
		{
			transaction = Transaction.Deserialize(bytes);
		}
		catch (Exception error) when (error is ArgumentException or
			InvalidDataException or IndexOutOfRangeException)
		{
			throw new InvalidDataException(
				"Jupiter returned an invalid serialized Solana transaction.",
				error);
		}
		if (transaction.Instructions is not { Count: > 0 })
			throw new InvalidDataException(
				"Jupiter transaction contains no instructions.");

		var offset = 0;
		var signatureCount = ReadCompactLength(bytes, ref offset);
		var signaturesOffset = offset;
		var messageOffset = checked(signaturesOffset + signatureCount * 64);
		if (signatureCount < 1 || messageOffset >= bytes.Length)
			throw new InvalidDataException(
				"Jupiter transaction has an invalid signature section.");
		var message = bytes.AsSpan(messageOffset);
		var headerOffset = (message[0] & 0x80) == 0 ? 0 : 1;
		if (message.Length < headerOffset + 3)
			throw new InvalidDataException(
				"Jupiter transaction message header is truncated.");
		var requiredSignatures = message[headerOffset];
		if (requiredSignatures != signatureCount)
			throw new InvalidDataException(
				"Jupiter transaction signature count does not match its " +
				"message header.");
		var accountOffset = headerOffset + 3;
		var accountCount = ReadCompactLength(message, ref accountOffset);
		if (requiredSignatures > accountCount ||
			accountOffset + checked(accountCount * PublicKey.PublicKeyLength) >
				message.Length)
			throw new InvalidDataException(
				"Jupiter transaction account list is truncated.");
		var walletBytes = _account.PublicKey.KeyBytes;
		var signerIndex = -1;
		for (var index = 0; index < requiredSignatures; index++)
		{
			var key = message.Slice(accountOffset +
				index * PublicKey.PublicKeyLength, PublicKey.PublicKeyLength);
			if (!key.SequenceEqual(walletBytes))
				continue;
			if (signerIndex >= 0)
				throw new InvalidDataException(
					"Jupiter transaction contains duplicate wallet signer slots.");
			signerIndex = index;
		}
		if (signerIndex < 0)
			throw new InvalidDataException(
				"Jupiter transaction does not contain a wallet " +
				"signature slot.");
		var messageBytes = message.ToArray();
		var signature = _account.Sign(messageBytes);
		if (!_account.PublicKey.Verify(messageBytes, signature))
			throw new CryptographicException(
				"The Jupiter transaction signature could not be verified.");
		signature.CopyTo(bytes.AsSpan(signaturesOffset + signerIndex * 64, 64));
		return Convert.ToBase64String(bytes);
	}

	private static int ReadCompactLength(ReadOnlySpan<byte> data,
		ref int offset)
	{
		var result = 0;
		var shift = 0;
		for (var index = 0; index < 3; index++)
		{
			if ((uint)offset >= (uint)data.Length)
				throw new InvalidDataException(
					"Jupiter transaction compact length is truncated.");
			var value = data[offset++];
			result |= (value & 0x7f) << shift;
			if ((value & 0x80) == 0)
				return result;
			shift += 7;
		}
		throw new InvalidDataException(
			"Jupiter transaction compact length is invalid.");
	}

	protected override void DisposeManaged()
	{
		if (_account?.PrivateKey.KeyBytes is { } key)
			CryptographicOperations.ZeroMemory(key);
		base.DisposeManaged();
	}
}
