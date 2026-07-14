namespace StockSharp.BarChart;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Net;

using StockSharp.Localization;
using StockSharp.Messages;

using DataType = StockSharp.Messages.DataType;

partial class BarChartMessageAdapter
{
	private const string _baseUrl = "https://ondemand.websol.barchart.com";
	private const string _defaultTimeFormatRequest = "yyyyMMddHHmmss";
	private HttpClient _httpClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="BarChartMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BarChartMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	protected override ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient();

		return SendOutMessageAsync(new ConnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		return SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var url = new Url($"{_baseUrl}/getInstrumentDefinition.json");

		url.QueryString.Append("apikey", Token.UnSecure());

		if (!lookupMsg.SecurityId.SecurityCode.IsEmpty())
		{
			url.QueryString.Append("symbol", lookupMsg.SecurityId.SecurityCode);
		}
		else if (!lookupMsg.SecurityId.BoardCode.IsEmpty())
		{
			url.QueryString.Append("exchange", lookupMsg.SecurityId.BoardCode);
		}

		try
		{
			var response = await _httpClient.GetStringAsync(url.ToString(), cancellationToken);
			var doc = XDocument.Parse(response);

			var results = doc.Element("results");
			if (results != null)
			{
				foreach (var element in results.Elements("instrument"))
				{
					await SendOutMessageAsync(new SecurityMessage
					{
						SecurityId = new SecurityId
						{
							SecurityCode = element.Element("symbol")?.Value,
							BoardCode = element.Element("exchange")?.Value,
						},
						Name = element.Element("description")?.Value,
						OriginalTransactionId = lookupMsg.TransactionId,
						PriceStep = element.Element("tickIncrement")?.Value?.To<decimal?>(),
						Multiplier = element.Element("pointValue")?.Value?.To<decimal?>(),
						SecurityType = element.Element("type")?.Value switch
						{
							"Stock" => SecurityTypes.Stock,
							"Future" => SecurityTypes.Future,
							"Option" => SecurityTypes.Option,
							"Index" => SecurityTypes.Index,
							"Forex" => SecurityTypes.Currency,
							_ => null
						}
					}, cancellationToken);
				}
			}
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe && (mdMsg.Count != null || mdMsg.From != null || mdMsg.To != null))
		{
			var url = new Url($"{_baseUrl}/getHistory.json");

			url.QueryString
				.Append("apikey", Token.UnSecure())
				.Append("symbol", mdMsg.SecurityId.SecurityCode)
				.Append("type", "ticks")
				.Append("order", "asc");

			if (mdMsg.Count != null)
				url.QueryString.Append("maxRecords", mdMsg.Count.Value);

			if (mdMsg.From != null)
				url.QueryString.Append("startDate", mdMsg.From.Value.ToString(_defaultTimeFormatRequest));

			if (mdMsg.To != null)
				url.QueryString.Append("endDate", mdMsg.To.Value.ToString(_defaultTimeFormatRequest));

			var response = await _httpClient.GetStringAsync(url.ToString(), cancellationToken);
			var doc = XDocument.Parse(response);

			var results = doc.Element("results");
			if (results != null)
			{
				foreach (var element in results.Elements("tick"))
				{
					try
					{
						var msg = new ExecutionMessage
						{
							SecurityId = mdMsg.SecurityId,
							OriginalTransactionId = mdMsg.TransactionId,
							DataTypeEx = DataType.Ticks,
							ServerTime = element.Element("timestamp")?.Value?.ToDateTime("yyyy-MM-dd'T'HH:mm:ss") ?? default,
							TradePrice = element.Element("price")?.Value?.To<decimal>() ?? 0,
							TradeVolume = element.Element("volume")?.Value?.To<decimal>() ?? 0,
						};

						await SendOutMessageAsync(msg, cancellationToken);
					}
					catch (Exception ex)
					{
						await SendOutErrorAsync(ex, cancellationToken);
					}
				}
			}
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var tf = mdMsg.GetTimeFrame();

		string dataType;
		int? interval = null;

		if (tf == TimeSpan.FromMinutes(1))
		{
			dataType = "minutes";
			interval = 1;
		}
		else if (tf.TotalMinutes > 1 && tf.TotalMinutes < 1440)
		{
			dataType = "minutes";
			interval = (int)tf.TotalMinutes;
		}
		else if (tf == TimeSpan.FromDays(1))
		{
			dataType = "daily";
		}
		else
			throw new InvalidOperationException(LocalizedStrings.InvalidTimeFrame);

		var url = new Url($"{_baseUrl}/getHistory.json");

		url.QueryString
			.Append("apikey", Token.UnSecure())
			.Append("symbol", mdMsg.SecurityId.SecurityCode)
			.Append("type", dataType)
			.Append("order", "asc");

		if (interval.HasValue)
			url.QueryString.Append("interval", interval.Value);

		if (mdMsg.Count != null)
			url.QueryString.Append("maxRecords", mdMsg.Count.Value);

		if (mdMsg.From != null)
			url.QueryString.Append("startDate", mdMsg.From.Value.ToString(_defaultTimeFormatRequest));

		if (mdMsg.To != null)
			url.QueryString.Append("endDate", mdMsg.To.Value.ToString(_defaultTimeFormatRequest));

		var response = await _httpClient.GetStringAsync(url.ToString(), cancellationToken);
		var doc = XDocument.Parse(response);

		var results = doc.Element("results");
		if (results != null)
		{
			foreach (var element in results.Elements("quote"))
			{
				try
				{
					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						SecurityId = mdMsg.SecurityId,
						OriginalTransactionId = mdMsg.TransactionId,
						OpenTime = element.Element("timestamp")?.Value?.ToDateTime("yyyy-MM-dd'T'HH:mm:ss") ??
								  element.Element("tradingDay")?.Value?.ToDateTime("yyyy-MM-dd") ?? default,
						OpenPrice = element.Element("open")?.Value?.To<decimal>() ?? 0,
						HighPrice = element.Element("high")?.Value?.To<decimal>() ?? 0,
						LowPrice = element.Element("low")?.Value?.To<decimal>() ?? 0,
						ClosePrice = element.Element("close")?.Value?.To<decimal>() ?? 0,
						TotalVolume = element.Element("volume")?.Value?.To<decimal>() ?? 0,
						OpenInterest = element.Element("openInterest")?.Value?.To<decimal?>(),
						State = CandleStates.Finished,
					}, cancellationToken);
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(ex, cancellationToken);
				}
			}
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
