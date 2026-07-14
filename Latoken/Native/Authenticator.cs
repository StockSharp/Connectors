namespace StockSharp.LATOKEN.Native;

using System.Security.Cryptography;

class Authenticator(bool canSign, SecureString key, SecureString secret) : Disposable
{
	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().UTF8());

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; } = canSign;
	public SecureString Key { get; } = key;

	public const string HashAlgo = "HMAC-SHA512";

	public string MakeSign(string data)
	{
		var signature = _hasher.ComputeHash(data.UTF8());

		return signature.Digest().ToLowerInvariant();
	}
}