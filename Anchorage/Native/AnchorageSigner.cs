namespace StockSharp.Anchorage.Native;

sealed class AnchorageSigner
{
	private readonly Ed25519PrivateKeyParameters _privateKey;

	public AnchorageSigner(SecureString signingKey)
	{
		if (signingKey.IsEmpty())
			throw new ArgumentNullException(nameof(signingKey));
		byte[] seed;
		try
		{
			seed = Convert.FromHexString(signingKey.UnSecure().Trim());
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"Anchorage signing key must be a hexadecimal Ed25519 seed.",
				nameof(signingKey), error);
		}
		try
		{
			if (seed.Length != Ed25519PrivateKeyParameters.KeySize)
				throw new ArgumentException(
					$"Anchorage Ed25519 seed must contain " +
					$"{Ed25519PrivateKeyParameters.KeySize} bytes.",
					nameof(signingKey));
			_privateKey = new(seed, 0);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(seed);
		}
	}

	public string Sign(string timestamp, string method, string pathAndQuery,
		byte[] body)
	{
		timestamp = timestamp.ThrowIfEmpty(nameof(timestamp));
		method = method.ThrowIfEmpty(nameof(method)).ToUpperInvariant();
		pathAndQuery = pathAndQuery.ThrowIfEmpty(nameof(pathAndQuery));
		ArgumentNullException.ThrowIfNull(body);
		var prefix = Encoding.UTF8.GetBytes(timestamp + method + pathAndQuery);
		var data = new byte[prefix.Length + body.Length];
		Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
		Buffer.BlockCopy(body, 0, data, prefix.Length, body.Length);
		try
		{
			var signer = new Ed25519Signer();
			signer.Init(true, _privateKey);
			signer.BlockUpdate(data, 0, data.Length);
			return Convert.ToHexString(signer.GenerateSignature())
				.ToLowerInvariant();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(data);
		}
	}
}
