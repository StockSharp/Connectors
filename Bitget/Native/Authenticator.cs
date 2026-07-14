namespace StockSharp.Bitget.Native;

using System.Security.Cryptography;

class Authenticator : Disposable
{
	private readonly HashAlgorithm _hasher;

	public Authenticator(bool canSign, SecureString key, SecureString secret, SecureString passphrase, bool isDemo)
	{
		CanSign = canSign;
		Key = key;
		Secret = secret;
		Passphrase = passphrase;
		IsDemo = isDemo;
		_hasher = CanSign ? new HMACSHA256(secret.UnSecure().UTF8()) : null;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; }
	public SecureString Key { get; }
	public SecureString Secret { get; }
	public SecureString Passphrase { get; }
	public bool IsDemo { get; }

	public string Sign(string data)
	{
		if (!CanSign)
			throw new InvalidOperationException();

		return _hasher.ComputeHash(data.UTF8()).Base64();
	}
}