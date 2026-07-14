namespace StockSharp.Deribit.Native;

using System.Security.Cryptography;
using System.Text;

class Authenticator : Disposable
{
	private readonly IdGenerator _nonGen;
	private readonly HashAlgorithm _hasher;

	private static readonly Encoding _iso = Encoding.GetEncoding("ISO-8859-1");

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		CanSign = canSign;
		Key = key;
		Secret = secret;
		_hasher = CanSign ? new HMACSHA256(_iso.GetBytes(secret.UnSecure())) : null;
		_nonGen = new UTCMillisecondIdGenerator();
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public bool CanSign { get; }
	public SecureString Key { get; }
	public SecureString Secret { get; }

	public string Sign(string data, out string nonce, out long timestamp)
	{
		if (!CanSign)
			throw new InvalidOperationException();

		nonce = "fdbskmz9";
		timestamp = _nonGen.GetNextId();

		var paramsString = $"{timestamp}\n{nonce}\n{data}";

		var signature = _hasher
		                .ComputeHash(_iso.GetBytes(paramsString))
		                .Digest()
		                .ToLowerInvariant();
		
		return signature;
	}
}