namespace StockSharp.Bitmex.Native;

using System.Security;
using System.Security.Cryptography;

class Authenticator : Disposable
{
	//private readonly SecureString _secret;

	private readonly HashAlgorithm _hasher;

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		Key = key;
		//_secret = secret;

		CanSign = canSign;

		_hasher = CanSign ? new HMACSHA256(secret.UnSecure().ASCII()) : null;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public SecureString Key { get; }

	public bool CanSign { get; }

	public string Sign(Method method, string url, long nonce, string args)
	{
		if (!CanSign)
			throw new InvalidOperationException();

		var signature = _hasher
		                .ComputeHash((method.ToString().ToUpperInvariant() + url + nonce + args).UTF8())
		                .Digest()
		                .ToLowerInvariant();

		return signature;
	}
}