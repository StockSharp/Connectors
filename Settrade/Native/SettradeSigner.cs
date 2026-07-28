namespace StockSharp.Settrade.Native;

sealed class SettradeSigner
{
	private readonly ECPrivateKeyParameters _privateKey;

	public SettradeSigner(SecureString secret)
	{
		if (secret.IsEmpty())
			throw new ArgumentNullException(nameof(secret));
		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(secret.UnSecure().Trim());
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"Settrade app secret must be a base64-encoded P-256 scalar.",
				nameof(secret), error);
		}
		try
		{
			if (bytes.Length is < 1 or > 32)
				throw new ArgumentException(
					"Settrade app secret must contain a P-256 scalar of at most 32 bytes.",
					nameof(secret));
			var parameters = SecNamedCurves.GetByName("secp256r1");
			var domain = new ECDomainParameters(parameters.Curve,
				parameters.G, parameters.N, parameters.H,
				parameters.GetSeed());
			var scalar = new BigInteger(1, bytes);
			if (scalar.SignValue <= 0 ||
				scalar.CompareTo(parameters.N) >= 0)
				throw new ArgumentException(
					"Settrade app secret is outside the P-256 scalar range.",
					nameof(secret));
			_privateKey = new(scalar, domain);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	public string Sign(string appId, string parameters, long timestamp)
	{
		var content = $"{appId.ThrowIfEmpty(nameof(appId))}." +
			$"{parameters ?? string.Empty}.{timestamp}";
		var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
		signer.Init(true, _privateKey);
		var data = Encoding.UTF8.GetBytes(content);
		try
		{
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
