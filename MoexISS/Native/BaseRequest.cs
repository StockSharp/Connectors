namespace StockSharp.MoexISS.Native;

using System.Diagnostics;

abstract class BaseRequest<TResponse>
{
	private const string _baseUrl = "https://iss.moex.com/iss/";
	private readonly HttpClient _client;
	private readonly string _url;

	protected BaseRequest(HttpClient client, string url, Dictionary<string, object> query = default)
	{
		if (url.IsEmpty())
			throw new ArgumentNullException(nameof(url));

		_url = $"{_baseUrl}{url}{CompileDict(query)}";
		_client = client ?? throw new ArgumentNullException(nameof(client));
	}

	public async Task<TResponse> Get(CancellationToken token)
	{
		var fullUrl = _url;
		Debug.WriteLine(fullUrl);
		
		return JsonConvert.DeserializeObject<TResponse>(await _client.GetStringAsync(fullUrl, token));
	}

	private static string CompileDict(IDictionary<string, object> query)
	{
		if (query is null)
			return string.Empty;

		var result = query
			.Where(_ => _.Value != null && (_.Value is not string s || s.Length > 0))
			.Select(_ =>
			{
				if (_.Value is DateTime)
					return $"{_.Key}={_.Value:yyyy-MM-dd}";

				return $"{_.Key}={_.Value}";
			})
			.JoinAnd();

		if (result.IsEmpty())
			return string.Empty;

		return $"?{result}";
	}
}