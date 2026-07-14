namespace StockSharp.Alpaca.Native;

using System.Runtime.CompilerServices;

abstract class RestMarketDataClient : RestAlpacaClient
{
	protected RestMarketDataClient(SecureString key, SecureString secret)
		: base("https://data.alpaca.markets", key, secret)
	{
	}

	protected const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

	protected static IEnumerable<T> Deserialize<T>(dynamic result)
		where T : BaseEntity
		=> ((JToken)result)
		.DeserializeObject<IDictionary<string, IEnumerable<T>>>()
		.SelectMany(v => v.Value)
		.OrderBy(e => e.Time);

	protected async IAsyncEnumerable<T> MakePagingRequest<T>(string urlPart, Func<RestRequest> createRequest, Func<dynamic, IEnumerable<T>> deserialize, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		if (createRequest is null)
			throw new ArgumentNullException(nameof(createRequest));

		if (deserialize is null)
			throw new ArgumentNullException(nameof(deserialize));

		var request = createRequest();

		while (true)
		{
			dynamic result = await MakeRequest<object>(urlPart, request, cancellationToken);

			foreach (var item in deserialize(result))
			{
				cancellationToken.ThrowIfCancellationRequested();
				yield return item;
			}

			var token = (string)result.next_page_token;

			if (token.IsEmpty())
				break;

			request = createRequest();
			request.AddParameter("page_token", token);
		}
	}
}