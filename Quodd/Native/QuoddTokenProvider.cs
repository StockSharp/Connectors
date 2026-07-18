namespace StockSharp.Quodd.Native;

sealed class QuoddTokenProvider : IDisposable
{
	private readonly QuoddAuthenticationModes _mode;
	private readonly string _configuredToken;
	private readonly string _username;
	private readonly string _password;
	private readonly string _firmLogin;
	private readonly string _firmPassword;
	private readonly QuoddAuthClient _auth;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private string _token;
	private DateTime _refreshAtUtc;

	public QuoddTokenProvider(QuoddAuthenticationModes mode, Uri authenticationAddress,
		string configuredToken, string username, string password, string firmLogin,
		string firmPassword)
	{
		_mode = mode;
		_configuredToken = configuredToken;
		_username = username;
		_password = password;
		_firmLogin = firmLogin;
		_firmPassword = firmPassword;
		if (mode != QuoddAuthenticationModes.Token)
			_auth = new(authenticationAddress);
	}

	public async Task<string> GetToken(CancellationToken cancellationToken)
	{
		await _refreshGate.WaitAsync(cancellationToken);
		try
		{
			var now = DateTime.UtcNow;
			if (!_token.IsEmpty() && now < _refreshAtUtc)
				return _token;

			_token = _mode switch
			{
				QuoddAuthenticationModes.Token =>
					_configuredToken.ThrowIfEmpty(nameof(_configuredToken)),
				QuoddAuthenticationModes.Trial =>
					await _auth.GetTrialToken(_username, _password, cancellationToken),
				QuoddAuthenticationModes.Firm =>
					await _auth.GetFirmToken(_username, _firmLogin, _firmPassword,
						cancellationToken),
				_ => throw new ArgumentOutOfRangeException(nameof(_mode), _mode, null),
			};
			_refreshAtUtc = _mode == QuoddAuthenticationModes.Token
				? DateTime.MaxValue : now.AddHours(23);
			return _token;
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	public void Invalidate()
	{
		if (_mode == QuoddAuthenticationModes.Token)
			return;

		_refreshGate.Wait();
		try
		{
			_refreshAtUtc = DateTime.MinValue;
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	public void Dispose()
	{
		_auth?.Dispose();
		_refreshGate.Dispose();
	}
}
