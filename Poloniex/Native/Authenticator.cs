namespace StockSharp.Poloniex.Native;

sealed class Authenticator : Disposable
{
	private readonly byte[] _secret;
	private long _lastTimestamp;

	public Authenticator(bool canSign, SecureString key, SecureString secret)
	{
		CanSign = canSign;
		Key = key;
		_secret = CanSign ? secret.UnSecure().UTF8() : null;
	}

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);

		base.DisposeManaged();
	}

	public bool CanSign { get; }
	public SecureString Key { get; }

	public long GetTimestamp()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastTimestamp);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var next = Math.Max(now, current + 1);

			if (Interlocked.CompareExchange(ref _lastTimestamp, next, current) == current)
				return next;
		}
	}

	public string Sign(string data)
	{
		if (!CanSign)
			throw new InvalidOperationException("Poloniex credentials are required for signing.");

		return Convert.ToBase64String(HMACSHA256.HashData(_secret, data.UTF8()));
	}
}
