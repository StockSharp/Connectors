namespace StockSharp.Cex.Native;

using System.Security.Cryptography;

class Authenticator : Disposable
{
	private readonly SecureString _key;
	//private readonly SecureString _secret;

	private readonly HashAlgorithm _hasher;

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		_key = key;
		//_secret = secret;

		CanSign = canSign;

		_hasher = CanSign ? new HMACSHA256(secret.UnSecure().ASCII()) : null;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; }

	public object Sign()
	{
		if (!CanSign)
			throw new InvalidOperationException();

		var keyStr = _key.UnSecure();
		var timestamp = (long)DateTime.UtcNow.ToUnix();

		var signature = _hasher
		                .ComputeHash((timestamp + keyStr).UTF8())
		                .Digest()
		                .ToLowerInvariant();
		
		return new
		{
			key = keyStr,
			signature,
			timestamp,
		};
	}
}