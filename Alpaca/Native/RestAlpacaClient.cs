namespace StockSharp.Alpaca.Native;

using Ecng.Reflection;

abstract class RestAlpacaClient : BaseLogReceiver
{
	private readonly string _address;
	private readonly SecureString _key;
	private readonly SecureString _secret;

	protected RestAlpacaClient(string address, SecureString key, SecureString secret)
	{
		_address = address.ThrowIfEmpty(nameof(address));
		_key = key ?? throw new ArgumentNullException(nameof(key));
		_secret = secret ?? throw new ArgumentNullException(nameof(secret));
	}

	protected static RestRequest CreateRequest(Method method)
		=> new() { Method = method };

	protected Task MakeRequest(string urlPart, RestRequest request, CancellationToken cancellationToken)
		=> MakeRequest<VoidType>(urlPart, request, cancellationToken);

	protected async Task<T> MakeRequest<T>(string urlPart, RestRequest request, CancellationToken cancellationToken)
	{
		if (urlPart.IsEmpty())	throw new ArgumentNullException(nameof(urlPart));
		if (request is null)	throw new ArgumentNullException(nameof(request));

		RestRequest ApplySecret(RestRequest request)
			=> request
				.AddHeader("APCA-API-KEY-ID", _key.UnSecure())
				.AddHeader("APCA-API-SECRET-KEY", _secret.UnSecure())
			;

		var isVoid = typeof(T) == typeof(VoidType);

		dynamic obj = await ApplySecret(request)
			.InvokeAsync($"{_address}/{urlPart}".To<Uri>(), this, this.AddVerboseLog, cancellationToken, throwIfEmptyResponse: !isVoid);

		if (isVoid)
			return default;

		return ((JToken)obj).DeserializeObject<T>();
	}
}