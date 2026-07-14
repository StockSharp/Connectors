namespace StockSharp.Bitmart.Native;

using System.Security;
using System.Security.Cryptography;

class Authenticator : Disposable
{
	public enum AuthKind
	{
		None,
		Keyed,
		Signed
	}

	private readonly HashAlgorithm _hasher;
	private readonly SecureString _key;
	private readonly string _memo;

	public Authenticator(SecureString key, SecureString secret, string memo)
	{
		_key = key;
		_memo = memo;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
	}

	public SecureString Key => _key;
	public bool CanSign => _hasher is not null;

    protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public (string key, string timestamp, string sign) Sign(string data)
	{
		var key = _key.UnSecure();
		var ts = ((long)DateTime.UtcNow.ToUnix(false)).ToString();
		var sing = _hasher.ComputeHash($"{ts}#{_memo}#{data}".UTF8()).Digest().ToLowerInvariant();
		return (key, ts, sing);
	}
}