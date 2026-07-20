namespace StockSharp.Drift.Native;

sealed class DriftSigner : Disposable
{
	private const int _maximumTransactionLength = 64 * 1024;
	private readonly Account _account;

	public DriftSigner(string walletAddress, SecureString privateKey)
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
					"The configured Drift wallet does not match the private key.",
					nameof(walletAddress));
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
				"A Solana private key is required for Drift trading.");
		encoded = encoded.ThrowIfEmpty(nameof(encoded)).Trim();
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(encoded);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Drift returned an invalid base64 transaction.", error);
		}
		if (bytes.Length is < 1 or > _maximumTransactionLength)
			throw new InvalidDataException(
				"Drift returned a transaction with an invalid size.");

		Transaction transaction;
		try
		{
			transaction = Transaction.Deserialize(bytes);
		}
		catch (Exception error) when (error is ArgumentException or
			InvalidDataException or IndexOutOfRangeException)
		{
			throw new InvalidDataException(
				"Drift returned an invalid serialized Solana transaction.",
				error);
		}
		if (transaction.Instructions is not { Count: > 0 })
			throw new InvalidDataException(
				"Drift transaction contains no instructions.");

		var offset = 0;
		var signatureCount = ReadCompactLength(bytes, ref offset);
		var signaturesOffset = offset;
		var messageOffset = checked(signaturesOffset + signatureCount * 64);
		if (signatureCount != 1 || messageOffset >= bytes.Length)
			throw new InvalidDataException(
				"Drift transaction must contain exactly one signature slot.");

		var message = bytes.AsSpan(messageOffset);
		var messageCursor = 0;
		if ((message[0] & 0x80) != 0)
		{
			if ((message[0] & 0x7f) != 0)
				throw new InvalidDataException(
					"Drift returned an unsupported Solana message version.");
			messageCursor++;
		}
		if (message.Length < messageCursor + 3)
			throw new InvalidDataException(
				"Drift transaction message header is truncated.");
		var requiredSignatures = message[messageCursor];
		messageCursor += 3;
		if (requiredSignatures != 1)
			throw new InvalidDataException(
				"Drift transaction requires unexpected signers.");
		var accountCount = ReadCompactLength(message, ref messageCursor);
		var accountsOffset = messageCursor;
		var accountsLength = checked(accountCount * PublicKey.PublicKeyLength);
		if (accountCount < 1 || accountsOffset + accountsLength + 32 >
			message.Length)
			throw new InvalidDataException(
				"Drift transaction account list is truncated.");
		if (!message.Slice(accountsOffset, PublicKey.PublicKeyLength)
			.SequenceEqual(_account.PublicKey.KeyBytes))
			throw new InvalidDataException(
				"Drift transaction signer does not match the configured wallet.");

		var velocityProgram = Encoders.Base58.DecodeData(
			DriftExtensions.VelocityProgramAddress);
		var velocityProgramIndex = -1;
		for (var index = 0; index < accountCount; index++)
			if (message.Slice(accountsOffset +
				index * PublicKey.PublicKeyLength, PublicKey.PublicKeyLength)
				.SequenceEqual(velocityProgram))
			{
				velocityProgramIndex = index;
				break;
			}
		if (velocityProgramIndex < 0)
			throw new InvalidDataException(
				"Drift transaction does not reference the Velocity program.");

		messageCursor = accountsOffset + accountsLength + 32;
		var instructionCount = ReadCompactLength(message, ref messageCursor);
		var hasVelocityInstruction = false;
		for (var index = 0; index < instructionCount; index++)
		{
			if ((uint)messageCursor >= (uint)message.Length)
				throw new InvalidDataException(
					"Drift transaction instruction is truncated.");
			var programIndex = message[messageCursor++];
			var instructionAccounts = ReadCompactLength(message,
				ref messageCursor);
			messageCursor = checked(messageCursor + instructionAccounts);
			var dataLength = ReadCompactLength(message, ref messageCursor);
			messageCursor = checked(messageCursor + dataLength);
			if (messageCursor > message.Length)
				throw new InvalidDataException(
					"Drift transaction instruction is truncated.");
			hasVelocityInstruction |= programIndex == velocityProgramIndex;
		}
		if (!hasVelocityInstruction)
			throw new InvalidDataException(
				"Drift transaction contains no Velocity instruction.");

		var messageBytes = message.ToArray();
		var signature = _account.Sign(messageBytes);
		if (!_account.PublicKey.Verify(messageBytes, signature))
			throw new CryptographicException(
				"The Drift transaction signature could not be verified.");
		signature.CopyTo(bytes.AsSpan(signaturesOffset, 64));
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
					"Drift transaction compact length is truncated.");
			var value = data[offset++];
			result |= (value & 0x7f) << shift;
			if ((value & 0x80) == 0)
				return result;
			shift += 7;
		}
		throw new InvalidDataException(
			"Drift transaction compact length is invalid.");
	}

	protected override void DisposeManaged()
	{
		if (_account?.PrivateKey.KeyBytes is { } key)
			CryptographicOperations.ZeroMemory(key);
		base.DisposeManaged();
	}
}
