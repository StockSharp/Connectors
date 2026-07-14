namespace StockSharp.Huobi.Native;

using System.Security.Cryptography;

class Authenticator : Disposable
{
	private readonly HashAlgorithm _hasher;

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		CanSign = canSign;
		Key = key;
		_hasher = CanSign ? new HMACSHA256(secret.UnSecure().UTF8()) : null;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; }
	public SecureString Key { get; }

	public const string Method = "HmacSHA256";
	public const string Version2 = "2";
	public const string Version21 = "2.1";

	public static string GetTimestamp() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

	public string Sign(Method method, string host, string path, string payload)
	{
		if (!CanSign)
			throw new InvalidOperationException();

		return _hasher
			.ComputeHash($"{method.ToString().ToUpperInvariant()}\n{host}\n{path}\n{payload}".UTF8())
			.Base64();
	}
}