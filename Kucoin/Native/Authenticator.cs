namespace StockSharp.Kucoin.Native;

using System.Security;
using System.Security.Cryptography;

class Authenticator : Disposable
{
	private static readonly JsonSerializerSettings _serializerSettings = JsonHelper.CreateJsonSerializerSettings();

	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;
	private readonly SecureString _passphrase;

	public Authenticator(SecureString key, SecureString secret, SecureString passphrase)
    {
		_passphrase = passphrase;
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public void Sign(RestRequest request, Uri url, object body)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		if (url is null)
			throw new ArgumentNullException(nameof(url));

		string bodyStr;

		if (body != null)
		{
			bodyStr = JsonConvert.SerializeObject(body, _serializerSettings);
			request.AddBodyAsStr(bodyStr);
		}
		else
			bodyStr = null;

		var timestamp = (long)DateTime.UtcNow.ToUnix(false);

		var strForSign = $"{timestamp}{request.Method.ToString().ToUpperInvariant()}{url.PathAndQuery}{bodyStr}";

		string sign(string input)
			=> _hasher
			.ComputeHash(input.UTF8())
			.Base64();

		request
			.AddHeader("KC-API-KEY", _key.UnSecure())
			.AddHeader("KC-API-SIGN", sign(strForSign))
			.AddHeader("KC-API-TIMESTAMP", timestamp.ToString())
			.AddHeader("KC-API-PASSPHRASE", sign(_passphrase.UnSecure()))
			.AddHeader("KC-API-KEY-VERSION", 2);
	}
}
