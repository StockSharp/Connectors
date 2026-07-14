namespace StockSharp.AlphaVantage;

using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Ecng.IO;
using Ecng.Collections;
using Ecng.Common;

using StockSharp.AlphaVantage.Native;
using StockSharp.Messages;

partial class AlphaVantageMessageAdapter
{
	private readonly HttpClient _httpClient = new();
	private readonly AlphaClient _alphaClient;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();

		if (secTypes.IsEmpty())
			secTypes.Add(SecurityTypes.Stock);

		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var secType in secTypes)
		{
			switch (secType)
			{
				case SecurityTypes.Stock:
				{
					if (lookupMsg.SecurityId.SecurityCode.IsEmpty())
						break;

					foreach (var symbol in await _alphaClient.Lookup(lookupMsg.SecurityId.SecurityCode, Token, cancellationToken))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var (currency, error) = symbol.Currency.FromMicexCurrencyName();

						var secMsg = new SecurityMessage
						{
							SecurityId = new()
							{
								SecurityCode = symbol.Code,
								BoardCode = BoardCodes.AlphaVantage,
							},
							Name = symbol.Name,
							SecurityType = symbol.Type.ToSecurityType(),
							Currency = currency,
							OriginalTransactionId = lookupMsg.TransactionId,
						};

						if (error is not null)
							await SendOutErrorAsync(error, cancellationToken);

						if (!secMsg.IsMatch(lookupMsg, secTypes))
							continue;

						await SendOutMessageAsync(secMsg, cancellationToken);

						if (--left <= 0)
							break;
					}

					break;
				}

				case SecurityTypes.Currency:
				case SecurityTypes.CryptoCurrency:
				{
					var fileName = secType == SecurityTypes.CryptoCurrency ? "digital" : "physical";

					var csv = await _httpClient.GetStringAsync($"https://www.alphavantage.co/{fileName}_currency_list/", cancellationToken);

					var reader = new FastCsvReader(csv, StringHelper.RN) { ColumnSeparator = ',' };
					if (await reader.NextLineAsync(cancellationToken))
					{
						while (await reader.NextLineAsync(cancellationToken))
						{
							var symbolFrom = reader.ReadString();

							if (symbolFrom.EqualsIgnoreCase("USD"))
								continue;

							var secMsg = new SecurityMessage
							{
								SecurityId = new()
								{
									SecurityCode = $"{symbolFrom}/USD",
									BoardCode = BoardCodes.AlphaVantage,
								},
								Name = reader.ReadString(),
								SecurityType = secType,
								OriginalTransactionId = lookupMsg.TransactionId,
							};

							if (!secMsg.IsMatch(lookupMsg, secTypes))
								continue;

							await SendOutMessageAsync(secMsg, cancellationToken);

							if (--left <= 0)
								break;
						}
					}

					break;
				}
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var from = mdMsg.From.Value;
		var to = mdMsg.To.Value;
		var left = mdMsg.Count ?? long.MaxValue;
		var tf = mdMsg.GetTimeFrame();
		var months = tf.IsIntraday() ? from.GetMonths(to) : [string.Empty];
		var secType = mdMsg.SecurityType ?? SecurityTypes.Stock;

		foreach (var month in months)
		{
			var candles = (await _alphaClient.GetCandles(mdMsg.SecurityId.SecurityCode, secType, tf, month, Token, cancellationToken)).OrderBy(p => p.Key);
			var noData = true;

			foreach (var (time, ohlc) in candles)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (time < from)
					continue;

				if (time > to)
				{
					noData = true;
					break;
				}

				noData = false;

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					DataType = mdMsg.DataType2,
					OpenTime = time,
					OpenPrice = ohlc.Open?.ToDecimal() ?? 0,
					HighPrice = ohlc.High?.ToDecimal() ?? 0,
					LowPrice = ohlc.Low?.ToDecimal() ?? 0,
					ClosePrice = ohlc.Close?.ToDecimal() ?? 0,
					TotalVolume = ohlc.Volume?.ToDecimal() ?? 0,
					State = CandleStates.Finished,
				}, cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0 || noData)
				break;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
