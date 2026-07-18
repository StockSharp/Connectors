namespace StockSharp.Backpack.Native;

sealed class BackpackSigner
{
	private readonly Ed25519PrivateKeyParameters _privateKey;

	public BackpackSigner(string privateKey)
	{
		byte[] seed;
		try
		{
			seed = Convert.FromBase64String(privateKey.ThrowIfEmpty(nameof(privateKey)));
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"Backpack Exchange private key must be a Base64-encoded ED25519 seed.",
				nameof(privateKey), error);
		}
		if (seed.Length != Ed25519PrivateKeyParameters.KeySize)
			throw new ArgumentException(
				$"Backpack Exchange ED25519 seed must contain {Ed25519PrivateKeyParameters.KeySize} bytes.",
				nameof(privateKey));
		_privateKey = new(seed, 0);
		Array.Clear(seed);
	}

	public string Sign(string payload)
	{
		var data = payload.ThrowIfEmpty(nameof(payload)).UTF8();
		var signer = new Ed25519Signer();
		signer.Init(true, _privateKey);
		signer.BlockUpdate(data, 0, data.Length);
		return Convert.ToBase64String(signer.GenerateSignature());
	}
}
