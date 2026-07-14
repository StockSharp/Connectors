namespace StockSharp.Deribit.Native;

class HttpClient : BaseLogReceiver
{
	private readonly string _baseUrl;
	private const string _version = "v2";

	public HttpClient(string address)
	{
		_baseUrl = $"https://{address}/api";
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Deribit) + "_" + nameof(HttpClient);

	public Task<IEnumerable<DeribitCurrency>> GetCurrencies(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<DeribitCurrency>>(CreateUrl("public/get_currencies"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Symbol>> GetInstruments(string currency, CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl($"public/get_instruments?currency={currency}"), CreateRequest(Method.Get), cancellationToken);
	}

	public async Task<IEnumerable<Trade>> GetTrades(string instrument, long? start, long? end, int? count, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("instrument_name", instrument)
			.AddParameter("sorting", "asc");

		if (start != null)
			request.AddParameter("start_timestamp", start.Value);

		if (end != null)
			request.AddParameter("end_timestamp", end.Value);

		if (count != null)
			request.AddParameter("count", count.Value);

		dynamic response = await MakeRequest<object>(CreateUrl("public/get_last_trades_by_instrument_and_time"), request, cancellationToken);

		return ((JToken)response.trades).DeserializeObject<IEnumerable<Trade>>();
	}

	public async Task<IEnumerable<Ohlc>> GetCandles(string instrument, long? start, long? end, string resolution, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("instrument_name", instrument)
			.AddParameter("start_timestamp", start.Value)
			.AddParameter("end_timestamp", end.Value)
			.AddParameter("resolution", resolution);

		dynamic response = await MakeRequest<object>(CreateUrl("public/get_tradingview_chart_data"), request, cancellationToken);

		var open = ((JArray)response.open).DeserializeObject<double[]>();
		var high = ((JArray)response.high).DeserializeObject<double[]>();
		var low = ((JArray)response.low).DeserializeObject<double[]>();
		var close = ((JArray)response.close).DeserializeObject<double[]>();
		var volume = ((JArray)response.volume).DeserializeObject<double[]>();
		var ticks = ((JArray)response.ticks).DeserializeObject<long[]>();
		//var status = ((JArray)response.status).DeserializeObject<string[]>();

		var candles = new List<Ohlc>();

		for (int i = 0; i < open.Length; i++)
		{
			//if (status[i] == "no_data")
			//	continue;

			candles.Add(new Ohlc
			{
				Open = open[i],
				High = high[i],
				Low = low[i],
				Close = close[i],
				Volume = volume[i],
				Tick = ticks[i],
			});
		}

		return candles;
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{_version}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject && obj.success == false)
			throw new InvalidOperationException((string)obj.message);

		return ((JToken)obj.result).DeserializeObject<T>();
	}
}