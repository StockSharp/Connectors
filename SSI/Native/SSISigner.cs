namespace StockSharp.SSI.Native;

sealed class SSISigner : IDisposable
{
	private readonly RSA _rsa;

	public SSISigner(SecureString privateKey)
	{
		var encoded = privateKey.UnSecure()
			.ThrowIfEmpty(nameof(privateKey));
		byte[] xmlBytes;
		try
		{
			xmlBytes = Convert.FromBase64String(encoded);
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"SSI private key must be a Base64-encoded RSAKeyValue.",
				nameof(privateKey), error);
		}
		XElement root;
		try
		{
			root = XElement.Parse(Encoding.UTF8.GetString(xmlBytes));
			_rsa = RSA.Create();
			_rsa.ImportParameters(new()
			{
				Modulus = Read(root, "Modulus"),
				Exponent = Read(root, "Exponent"),
				P = Read(root, "P"),
				Q = Read(root, "Q"),
				DP = Read(root, "DP"),
				DQ = Read(root, "DQ"),
				InverseQ = Read(root, "InverseQ"),
				D = Read(root, "D"),
			});
		}
		catch (Exception error)
			when (error is not ArgumentException)
		{
			_rsa?.Dispose();
			throw new ArgumentException(
				"SSI private key contains an invalid RSAKeyValue.",
				nameof(privateKey), error);
		}
	}

	private static byte[] Read(XElement root, string name)
		=> Convert.FromBase64String(root.Element(name)?.Value
			.ThrowIfEmpty(name));

	public string Sign(string data)
		=> Convert.ToHexString(_rsa.SignData(
			Encoding.UTF8.GetBytes(data ?? string.Empty),
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
			.ToLowerInvariant();

	public void Dispose() => _rsa.Dispose();
}
