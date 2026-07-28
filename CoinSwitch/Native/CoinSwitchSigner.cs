namespace StockSharp.CoinSwitch.Native;

sealed class CoinSwitchSigner : IDisposable
{
	private readonly Ed25519PrivateKeyParameters _privateKey;

	public CoinSwitchSigner(SecureString secret)
	{
		if (secret.IsEmpty())
			throw new ArgumentNullException(nameof(secret));
		byte[] seed;
		try
		{
			seed = Convert.FromHexString(secret.UnSecure().Trim());
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"CoinSwitch secret must be a hexadecimal " +
					"Ed25519 seed.",
				nameof(secret),
				error);
		}
		try
		{
			if (seed.Length != Ed25519PrivateKeyParameters.KeySize)
				throw new ArgumentException(
					"CoinSwitch Ed25519 secret must contain " +
						$"{Ed25519PrivateKeyParameters.KeySize} bytes.",
					nameof(secret));
			_privateKey = new(seed, 0);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(seed);
		}
	}

	public string Sign(
		string method,
		string pathAndQuery,
		long epoch)
	{
		var data = Encoding.UTF8.GetBytes(
			CreateMessage(method, pathAndQuery, epoch));
		try
		{
			var signer = new Ed25519Signer();
			signer.Init(true, _privateKey);
			signer.BlockUpdate(data, 0, data.Length);
			return Convert.ToHexString(
				signer.GenerateSignature()).ToLowerInvariant();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(data);
		}
	}

	internal static string CreateMessage(
		string method,
		string pathAndQuery,
		long epoch)
		=> method.ThrowIfEmpty(nameof(method)).ToUpperInvariant() +
			Uri.UnescapeDataString(
				pathAndQuery.ThrowIfEmpty(
					nameof(pathAndQuery))
					.Replace("+", " ", StringComparison.Ordinal)) +
			epoch.ToString(CultureInfo.InvariantCulture);

	public void Dispose()
	{
	}
}
