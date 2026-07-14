namespace StockSharp.GateIO.Native;

using System.Security.Cryptography;

class Authenticator : Disposable
{
	private readonly HashAlgorithm _hasher;

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		CanSign = canSign;
		Key = key;
		Secret = secret;
		_hasher = CanSign ? new HMACSHA512(secret.UnSecure().UTF8()) : null;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; }
	public SecureString Key { get; }
	public SecureString Secret { get; }

	public string Sign(string data)
	{
		if (!CanSign)
			throw new InvalidOperationException();

		return _hasher
			.ComputeHash(data.UTF8())
			.Digest()
			.ToLowerInvariant();
	}
}