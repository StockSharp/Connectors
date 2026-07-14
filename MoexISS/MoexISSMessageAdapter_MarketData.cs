namespace StockSharp.MoexISS;

using System.Globalization;

using StockSharp.MoexISS.Native.Requests;

public partial class MoexISSMessageAdapter
{
	private readonly HttpClient _client = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var secTypesModified = secTypes.ToHashSet();

		if (secTypesModified.IsEmpty())
		{
			secTypesModified.Add(SecurityTypes.Stock);
			secTypesModified.Add(SecurityTypes.Future);

			//// scan whole market if we not look some specified stock
			//if (secCode.IsEmpty())
			//{
			//	secTypesModified.Add(SecurityTypes.Currency);
			//	secTypesModified.Add(SecurityTypes.Bond);
			//	secTypesModified.Add(SecurityTypes.Commodity);
			//	secTypesModified.Add(SecurityTypes.Swap);
			//	secTypesModified.Add(SecurityTypes.Weather);
			//	secTypesModified.Add(SecurityTypes.Warrant);
			//	secTypesModified.Add(SecurityTypes.Indicator);
			//}
		}

		foreach (var secType in secTypesModified)
		{
			var engine = GetEngine(secType);

			var marketsResponse = (await new MarketsRequest(_client, engine).Get(token)).Markets;

			var marketColumns = marketsResponse.Columns;
			var marketNameIdx = marketColumns.IndexOf(c => c.EqualsIgnoreCase("name"));

			foreach (var market in marketsResponse.Data)
			{
				var marketCode = market[marketColumns[marketNameIdx]];

				if (!secTypesModified.Contains(SecurityTypes.Option) && marketCode.EqualsIgnoreCase("options"))
					continue;

				if (!secTypesModified.Contains(SecurityTypes.Repo) && (marketCode.EqualsIgnoreCase("ccp") || marketCode.EqualsIgnoreCase("repo") || marketCode.EqualsIgnoreCase("gcc")))
					continue;

				var securitiesResponse = (await new MarketSecuritiesListRequest(_client, engine, marketCode, default, lookupMsg.GetUnderlyingCode()).Get(token))?.Securities;

				if (securitiesResponse is null)
					continue;

				var securitiesColumns = securitiesResponse.Columns;

				int GetIdx(string name)
					=> securitiesColumns.IndexOf(c => c.EqualsIgnoreCase(name));

				var symbolIdx = GetIdx("secid");
				var boardIdx = GetIdx("boardId");
				var shortIdx = GetIdx("shortname");
				var nameIdx = GetIdx("secname");
				var faceValueIdx = GetIdx("facevalue");
				var lotIdx = GetIdx("lotsize");
				var decimalsIdx = GetIdx("decimals");
				var minStepIdx = GetIdx("minstep");
				var lastTradeDateIdx = GetIdx("lasttradedate");
				var issueSizeIdx = GetIdx("issuesize");
				var isinIdx = GetIdx("isin");
				var currencyIdx = GetIdx("currencyid");
				var secTypeIdx = GetIdx("sectype");
				var settleDateIdx = GetIdx("settledate");
				var optionTypeIdx = GetIdx("optiontype");
				var strikeIdx = GetIdx("strike");
				var assetCodeIdx = GetIdx("underlyingasset");
				var assetTypeIdx = GetIdx("underlyingtype");

				if (nameIdx == -1)
					nameIdx = GetIdx("name");

				if (lotIdx == -1)
					lotIdx = GetIdx("lotvolume");

				foreach (var security in securitiesResponse.Data)
				{
					token.ThrowIfCancellationRequested();

					T Get<T>(int index)
						=> GetDataValue<T>(security, securitiesColumns, index, "yyyy-MM-dd");

					var secMsg = new SecurityMessage
					{
						SecurityId = new()
						{
							SecurityCode = Get<string>(symbolIdx),
							BoardCode = Get<string>(boardIdx),
							Isin = Get<string>(isinIdx),
						},
						OriginalTransactionId = lookupMsg.TransactionId,
						ShortName = Get<string>(shortIdx),
						Name = Get<string>(nameIdx),
						Decimals = Get<int?>(decimalsIdx),
						Multiplier = Get<int?>(lotIdx),
						Currency = Get<string>(currencyIdx).FromMicexCurrencyName(ex => LogWarning(ex.Message)),
						FaceValue = Get<decimal?>(faceValueIdx),
						IssueSize = Get<decimal?>(issueSizeIdx),
						Strike = Get<decimal?>(strikeIdx),
						OptionType = (Get<string>(optionTypeIdx)?.ToUpperInvariant()) switch
						{
							"C" => OptionTypes.Call,
							"P" => OptionTypes.Put,
							_ => null,
						},
						ExpiryDate = Get<DateTime?>(lastTradeDateIdx),
						SettlementDate = Get<DateTime?>(settleDateIdx),
						UnderlyingSecurityType = (Get<string>(assetTypeIdx)?.ToUpperInvariant()) switch
						{
							"F" => SecurityTypes.Future,
							"S" => SecurityTypes.Stock,
							_ => null,
						},
						SecurityType = marketCode.ToLowerInvariant() switch
						{
							"forts" => SecurityTypes.Future,
							"options" => SecurityTypes.Option,
							_ => GetSecType(Get<string>(secTypeIdx)),
						},
					}.TryFillUnderlyingId(Get<string>(assetCodeIdx));

					if (!secMsg.IsMatch(lookupMsg, secTypes))
						continue;

					await SendOutMessageAsync(secMsg, token);

					if (--left <= 0)
						break;
				}

				if (left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var to = mdMsg.To.Value;
		var from = mdMsg.From.Value;
		var left = mdMsg.Count ?? long.MaxValue;

		var secCode = mdMsg.SecurityId.SecurityCode;
		var secType = mdMsg.SecurityType ?? SecurityTypes.Stock;
		var engine = GetEngine(secType);
		var market = GetDefaultMarket(secType);
		var interval = (int)mdMsg.GetTimeFrame().TotalMinutes;

		var start = default(int?);

		while (true)
		{
			var candlesResponse = (await new EngineMarketSecurityCandlesRequest(
				_client,
				engine,
				market,
				secCode,
				interval,
				from,
				start)
			.Get(cancellationToken)).Candles;

			var columns = candlesResponse.Columns;

			int GetIdx(string name)
				=> columns.IndexOf(c => c.EqualsIgnoreCase(name));

			var openIdx = GetIdx("open");
			var closeIdx = GetIdx("close");
			var highIdx = GetIdx("high");
			var lowIdx = GetIdx("low");
			var volumeIdx = GetIdx("volume");
			var beginIdx = GetIdx("begin");
			var endIdx = GetIdx("end");

			var noData = true;

			foreach (var candle in candlesResponse.Data)
			{
				cancellationToken.ThrowIfCancellationRequested();

				T Get<T>(int index)
					=> GetDataValue<T>(candle, columns, index, "yyyy-MM-dd HH:mm:ss");

				var openTime = Get<DateTime>(beginIdx);

				if (openTime < from)
					continue;

				if (openTime > to)
				{
					noData = true;
					break;
				}

				var tfc = new TimeFrameCandleMessage
				{
					OriginalTransactionId = transId,
					DataType = mdMsg.DataType2,

					State = CandleStates.Finished,

					OpenPrice = Get<decimal>(openIdx),
					ClosePrice = Get<decimal>(closeIdx),
					HighPrice = Get<decimal>(highIdx),
					LowPrice = Get<decimal>(lowIdx),
					TotalVolume = Get<decimal>(volumeIdx),

					OpenTime = openTime,
					CloseTime = Get<DateTime>(endIdx),
				};

				noData = false;

				await SendOutMessageAsync(tfc, cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0 || noData)
				break;

			start = (start ?? 0) + candlesResponse.Data.Count;

			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var divs = (await new SecurityDividendsRequest(_client, mdMsg.SecurityId.SecurityCode)
			.Get(cancellationToken)).Dividends;

		var columns = divs.Columns;

		int GetIdx(string name)
			=> columns.IndexOf(c => c.EqualsIgnoreCase(name));

		var dateIdx = GetIdx("registryclosedate");
		var valueIdx = GetIdx("value");
		var currencyIdx = GetIdx("currencyid");

		var left = mdMsg.Count ?? long.MaxValue;
		var from = mdMsg.From ?? DateTime.UtcNow;
		var to = mdMsg.To ?? DateTime.UtcNow;

		foreach (var div in divs.Data)
		{
			cancellationToken.ThrowIfCancellationRequested();

			T Get<T>(int index)
				=> GetDataValue<T>(div, columns, index, "yyyy-MM-dd");

			var date = Get<DateTime>(dateIdx);

			if (date < from)
				continue;

			if (date > to)
				break;

			var l1Msg = new Level1ChangeMessage
			{
				OriginalTransactionId = transId,
				ServerTime = date,
			}
			.Add(Level1Fields.Dividend, Get<decimal>(valueIdx))
			;

			await SendOutMessageAsync(l1Msg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}

	private static string GetEngine(SecurityTypes type)
		=> type switch
		{
			SecurityTypes.Currency => "currency",
			SecurityTypes.Bond => "state",
			SecurityTypes.Future => "futures",
			SecurityTypes.Option => "futures",
			SecurityTypes.Commodity => "commodity",
			SecurityTypes.Swap => "interventions",
			SecurityTypes.Weather => "agro",
			SecurityTypes.Warrant => "offboard",
			SecurityTypes.Indicator => "otc",
			_ => "stock",
		};

	private static string GetDefaultMarket(SecurityTypes type)
		=> type switch
		{
			SecurityTypes.Currency => "currency",
			SecurityTypes.Bond => "bonds",
			SecurityTypes.Future => "forts",
			SecurityTypes.Option => "options",
			SecurityTypes.Commodity => "futures",
			SecurityTypes.Swap => "grain",
			SecurityTypes.Weather => "sugar",
			SecurityTypes.Warrant => "bonds",
			SecurityTypes.Indicator => "bonds",
			SecurityTypes.Repo => "repo",
			_ => "shares",
		};

	private static T GetDataValue<T>(Dictionary<string, string> data, List<string> columns, int index, string dateFormat)
	{
		if (data is null)
			throw new ArgumentNullException(nameof(data));

		if (columns is null)
			throw new ArgumentNullException(nameof(columns));

		if (index == -1)
			return default;

		var str = data[columns[index]];

		if (str.IsEmpty())
			return default;

		object value;

		if (typeof(T) != typeof(string))
		{
			var type = typeof(T);
			type = type.GetUnderlyingType() ?? type;

			if (type == typeof(decimal))
				value = str.To<double>().ToDecimal();
			else if (type == typeof(DateTime))
				value = str.ToDateTime(dateFormat, CultureInfo.InvariantCulture).ApplyMoscow().UtcDateTime;
			else if (type == typeof(int))
				value = str.To<int>();
			else
				throw new InvalidOperationException(typeof(T).Name);
		}
		else
			value = str;

		return (T)value;
	}

	private static SecurityTypes? GetSecType(string secType)
	{
		if (secType.IsEmpty() || secType.Length != 1)
			return null;

		return secType[0] switch
		{
			'1' => SecurityTypes.Stock, // Акция обыкновенная
			'2' => SecurityTypes.Stock, // Акция привилегированная
			'3' => SecurityTypes.Bond, // Государственные облигации
			'4' => SecurityTypes.Bond, // Региональные облигации
			'5' => SecurityTypes.Bond, // Облигации центральных банков
			'6' => SecurityTypes.Bond, // Корпоративные облигации
			'7' => SecurityTypes.Bond, // Облигации МФО
			'8' => SecurityTypes.Bond, // Биржевые облигации
			'9' => SecurityTypes.Fund, // Паи открытых ПИФов
			'A' => SecurityTypes.Fund, // Паи интервальных ПИФов
			'B' => SecurityTypes.Fund, // Паи закрытых ПИФов
			'C' => SecurityTypes.Bond, // Муниципальные облигации
			'D' => SecurityTypes.Adr, // Депозитарные расписки
			'E' => SecurityTypes.Etf, // Бумаги иностранных инвестиционных фондов(ETF)
			'F' => SecurityTypes.Receipt, // Ипотечный сертификат
			'G' => SecurityTypes.Index, // Корзина бумаг
			'H' => SecurityTypes.Indicator, // Доп.идентификатор списка
			'I' => SecurityTypes.Etf, // ETC
			'J' => SecurityTypes.Fund, // Пай биржевых ПИФов
			'O' => SecurityTypes.Index, // Корзина РЕПО
			'P' => SecurityTypes.Commodity, // Драг.металлы
			'Q' => SecurityTypes.Currency, // Валюта
			'U' => SecurityTypes.Gdr, // Клиринговые сертификаты участия
			_ => null,
		};
	}
}
