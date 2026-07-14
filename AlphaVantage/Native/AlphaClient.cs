namespace StockSharp.AlphaVantage.Native;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Net;
using Ecng.Serialization;
using Ecng.Logging;

using Newtonsoft.Json.Linq;

using RestSharp;

using StockSharp.AlphaVantage.Native.Model;
using StockSharp.Localization;
using StockSharp.Messages;

class AlphaClient : BaseLogReceiver
{


	///// <summary>
	///// Possible functions of Alpha Vantage API
	///// </summary>
	//private enum ApiFunction
	//{
	//	// Stock Time Series Data
	//	TIME_SERIES_INTRADAY,
	//	TIME_SERIES_DAILY,
	//	TIME_SERIES_DAILY_ADJUSTED,
	//	TIME_SERIES_WEEKLY,
	//	TIME_SERIES_WEEKLY_ADJUSTED,
	//	TIME_SERIES_MONTHLY,
	//	TIME_SERIES_MONTHLY_ADJUSTED,
	//	BATCH_STOCK_QUOTES,
        
	//	// Foreign Exchange (FX)
	//	CURRENCY_EXCHANGE_RATE,
        
	//	// Digital & Crypto Currencies
	//	DIGITAL_CURRENCY_INTRADAY,
	//	DIGITAL_CURRENCY_DAILY,
	//	DIGITAL_CURRENCY_WEEKLY,
	//	DIGITAL_CURRENCY_MONTHLY,
        
	//	// Stock Technical Indicators
	//	SMA,
	//	EMA,
	//	WMA,
	//	DEMA,
	//	TEMA,
	//	TRIMA,
	//	KAMA,
	//	MAMA,
	//	T3,
	//	MACD,
	//	MACDEXT,
	//	STOCH,
	//	STOCHF,
	//	RSI,
	//	STOCHRSI,
	//	WILLR,
	//	ADX,
	//	ADXR,
	//	APO,
	//	PPO,
	//	MOM,
	//	BOP,
	//	CCI,
	//	CMO,
	//	ROC,
	//	ROCR,
	//	AROON,
	//	AROONOSC,
	//	MFI,
	//	TRIX,
	//	ULTOSC,
	//	DX,
	//	MINUS_DI,
	//	PLUS_DI,
	//	MINUS_DM,
	//	PLUS_DM,
	//	BBANDS,
	//	MIDPOINT,
	//	MIDPRICE,
	//	SAR,
	//	TRANGE,
	//	ATR,
	//	NATR,
	//	AD,
	//	ADOSC,
	//	OBV,
	//	HT_TRENDLINE,
	//	HT_SINE,
	//	HT_TRENDMODE,
	//	HT_DCPERIOD,
	//	HT_DCPHASE,
	//	HT_PHASOR,
        
	//	// Sector Performances
	//	SECTOR
	//}

	// https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=MSFT&interval=1min&apikey=demo
	private const string _baseUrl = "https://www.alphavantage.co/query";

	// to get readable name after obfuscation
	public override string Name => nameof(AlphaVantage) + "_" + nameof(AlphaClient);

	public async ValueTask<IEnumerable<Symbol>> Lookup(string symbolLike, SecureString token, CancellationToken cancellationToken)
	{
		if (symbolLike.IsEmpty())
			throw new ArgumentNullException(nameof(symbolLike));

		var request = CreateRequest();

		request
			.AddQueryParameter("function", "SYMBOL_SEARCH")
			.AddQueryParameter("keywords", symbolLike);

		dynamic response = await MakeRequest<object>(_baseUrl.To<Uri>(), ApplySecret(request, token), cancellationToken);

		var matches = response.bestMatches;

		if (matches is null)
			return [];

		return ((JToken)matches).DeserializeObject<IEnumerable<Symbol>>();
	}

	public async ValueTask<IDictionary<DateTime, Ohlc>> GetCandles(string symbol, SecurityTypes secType, TimeSpan interval, string month, SecureString token, CancellationToken cancellationToken)
	{
		var request = CreateRequest();

		string intervalArg = null;

		switch (interval.Ticks)
		{
			case TimeSpan.TicksPerDay:
				request.AddQueryParameter("function", secType switch
				{
					SecurityTypes.CryptoCurrency => "DIGITAL_CURRENCY_DAILY",
					SecurityTypes.Currency => "FX_DAILY",
					_ => "TIME_SERIES_DAILY",
				});
				break;
			case TimeHelper.TicksPerWeek:
				request.AddQueryParameter("function", secType switch
				{
					SecurityTypes.CryptoCurrency => "DIGITAL_CURRENCY_WEEKLY",
					SecurityTypes.Currency => "FX_WEEKLY",
					_ => "TIME_SERIES_WEEKLY",
				});
				break;
			case TimeHelper.TicksPerMonth:
				request.AddQueryParameter("function", secType switch
				{
					SecurityTypes.CryptoCurrency => "DIGITAL_CURRENCY_MONTHLY",
					SecurityTypes.Currency => "FX_MONTHLY",
					_ => "TIME_SERIES_MONTHLY",
				});
				break;
			default:
				if (interval == TimeSpan.FromMinutes(1))
					intervalArg = "1min";
				else if (interval == TimeSpan.FromMinutes(5))
					intervalArg = "5min";
				else if (interval == TimeSpan.FromMinutes(15))
					intervalArg = "15min";
				else if (interval == TimeSpan.FromMinutes(30))
					intervalArg = "30min";
				else if (interval == TimeSpan.FromMinutes(60))
					intervalArg = "60min";
				else
					throw new ArgumentOutOfRangeException(nameof(interval), interval, LocalizedStrings.InvalidValue);

				request.AddQueryParameter("function", secType switch
				{
					SecurityTypes.CryptoCurrency => "CRYPTO_INTRADAY",
					SecurityTypes.Currency => "FX_INTRADAY",
					_ => "TIME_SERIES_INTRADAY",
				});

				if (!month.IsEmpty())
					request.AddQueryParameter(nameof(month), month);
				
				request.AddQueryParameter("outputsize", "full");

				break;
		}

		if (secType == SecurityTypes.CryptoCurrency)
		{
			var (s, m) = symbol.SplitToPair();

			request
				.AddQueryParameter("symbol", s)
				.AddQueryParameter("market", m);
		}
		else if (secType == SecurityTypes.Currency)
		{
			var (f, t) = symbol.SplitToPair();

			request
				.AddQueryParameter("from_symbol", f)
				.AddQueryParameter("to_symbol", t);
		}
		else
		{
			request.AddQueryParameter("symbol", symbol);
		}

		if (intervalArg != null)
		{
			request.AddQueryParameter("interval", intervalArg);
		}

		var dtFormat = interval.IsIntraday() ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd";

		var candles = new Dictionary<DateTime, Ohlc>();

		var response = await MakeRequest<JObject>(_baseUrl.To<Uri>(), ApplySecret(request, token), cancellationToken);

		var property = response.Properties().Last();

		if (property.Name.EqualsIgnoreCase("information"))
			throw new InvalidOperationException(property.Value.To<string>());

		foreach (JProperty prop in property.Value)
		{
			var obj = (JObject)prop.Value;

			double? GetValue(string name)
				=> (double?)obj.Properties().FirstOrDefault(p => p.Name.ContainsIgnoreCase(name))?.Value;

			candles.Add(prop.Name.ToDateTime(dtFormat).UtcKind(), new()
			{
				Open = GetValue("open"),
				High = GetValue("high"),
				Low = GetValue("low"),
				Close = GetValue("close"),
				Volume = GetValue("volume"),
			});
		}

		return candles;
	}

	private static RestRequest CreateRequest(Method method = Method.Get)
	{
		return new RestRequest((string)null, method);
	}

	private static RestRequest ApplySecret(RestRequest request, SecureString token)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		if (token.IsEmpty())
			throw new ArgumentNullException(nameof(token));

		return request.AddQueryParameter("apikey", token.UnSecure());
	}

	private async ValueTask<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		var content = await InvokeAsync(request, url, cancellationToken);

		dynamic obj = content.DeserializeObject<object>();

		if (((JToken)obj).Type == JTokenType.Object)
		{
			if ((obj.success != null && obj.success == false) || (obj.code != null && (int)obj.code != 0))
				throw new InvalidOperationException((string)obj.msg);
			else if (((JObject)obj).Property("Error Message") != null)
				throw new InvalidOperationException((string)((JObject)obj).Property("Error Message"));
		}

		return ((JToken)obj).DeserializeObject<T>();
	}

	private async Task<string> InvokeAsync(RestRequest request, Uri url, CancellationToken token)
	{
		request.Resource = url.IsAbsoluteUri ? url.AbsoluteUri : url.OriginalString;
		this.AddVerboseLog("Request {0}, '{1}' Args '{2}'.", request.Method, url, request.Parameters.ToQueryString(false));

		var client = new RestClient(new RestClientOptions
		{
			UserAgent = nameof(StockSharp),
		});

		var response = await client.ExecuteAsync(request, token);

		this.AddVerboseLog("Response code {0}.", response.StatusCode);

		if (response.StatusCode != HttpStatusCode.OK || response.Content.IsEmpty())
			throw response.ToError();

		return response.Content;
	}
}