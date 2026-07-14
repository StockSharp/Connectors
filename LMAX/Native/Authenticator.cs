namespace StockSharp.LMAX.Native;

using System.Security.Cryptography;

class Authenticator(SecureString clientKeyId, SecureString secretKey)
{
	private readonly SecureString _clientKeyId = clientKeyId ?? throw new ArgumentNullException(nameof(clientKeyId));
	private readonly SecureString _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));

    public SecureString ClientKeyId => _clientKeyId;

	public (string timestamp, string nonce, string signature) CreateSignature()
	{
		var timestamp = ((long)DateTime.UtcNow.ToUnix(false)).ToString();
		var nonce = Guid.NewGuid().ToString("N");

		var message = $"{timestamp}{nonce}";
		var signature = ComputeHmacSha256(message);

		return (timestamp, nonce, signature);
	}

	public string ComputeHmacSha256(string message)
	{
		using var hmac = new HMACSHA256(_secretKey.UnSecure().Base64());
		var hash = hmac.ComputeHash(message.UTF8());
		return hash.Base64();
	}
}