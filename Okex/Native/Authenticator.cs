namespace StockSharp.Okex.Native;

using System.Security.Cryptography;

using Newtonsoft.Json.Serialization;

class Authenticator : Disposable
{
	private static readonly JsonSerializerSettings _jsonSerializerSettings = new()
	{
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() }
	};

	private readonly HashAlgorithm _hasher;
	private readonly SecureString _key;
	private readonly SecureString _passphrase;
	private readonly bool _isDemo;

	public Authenticator(SecureString key, SecureString secret, SecureString passphrase, bool isDemo)
	{
		_key = key;
		_passphrase = passphrase;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_isDemo = isDemo;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	private string Sign(string ts, string url, Method method, string body)
		=> _hasher.ComputeHash($"{ts}{method.To<string>().ToUpperInvariant()}{url}{body}".UTF8()).Base64();

	public void ApplySecret(RestRequest request, Url url)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var requestData = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost)
			.ToDictionary(p => p.Name, p => p.Value);

		request.RemoveWhere(p => p.Type == ParameterType.GetOrPost);

		var body = string.Empty;

		if (request.Method == Method.Get)
		{
			foreach (var pair in requestData)
				url.QueryString[pair.Key] = pair.Value;
		}
		else
		{
			body = JsonConvert.SerializeObject(requestData, _jsonSerializerSettings);
			request.AddBodyAsStr(body);
		}

		var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

		request
			.AddHeader("OK-ACCESS-KEY", _key.UnSecure())
			.AddHeader("OK-ACCESS-SIGN", Sign(ts, url.PathAndQuery, request.Method, body))
			.AddHeader("OK-ACCESS-TIMESTAMP", ts)
			.AddHeader("OK-ACCESS-PASSPHRASE", _passphrase.UnSecure());

		if(_isDemo)
			request.AddHeader("x-simulated-trading", "1");
	}

	public object GetLoginArg(string endPoint, Method method)
	{
		var ts = DateTime.UtcNow.ToUnix().To<long>().To<string>();

		return new
		{
			apiKey       = _key.UnSecure(),
			passphrase   = _passphrase.UnSecure(),
			timestamp    = ts,
			sign         = Sign(ts, endPoint, method, string.Empty)
		};
	}
}
