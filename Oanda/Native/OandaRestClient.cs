namespace StockSharp.Oanda.Native;

class OandaRestClient(bool isDemo, SecureString token, bool useCompression)
{
	private readonly SecureString _token = token;
	private readonly string _restUrl = isDemo ? "https://api-fxpractice.oanda.com" : "https://api-fxtrade.oanda.com";
	private readonly bool _useCompression = useCompression;

	public async Task<IEnumerable<TradeData>> GetTradesAsync(string accountId, long? maxId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/trades");

		if (maxId != null)
			url.QueryString.Append("maxId", maxId.Value);

		return (await MakeRequestAsync<TradesResponse>(url, cancellationToken)).Trades;
	}

	public async Task<IEnumerable<Order>> GetOrdersAsync(string accountId, long? maxId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/orders");

		if (maxId != null)
			url.QueryString.Append("maxId", maxId.Value);

		return (await MakeRequestAsync<OrdersResponse>(url, cancellationToken)).Orders;
	}

	public async Task<IEnumerable<string>> GetTransactionPagesAsync(string accountId, string from, string to, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/transactions");

		url.QueryString
		   .Append("id", from);

		return (await MakeRequestAsync<TransactionPagesResponse>(url, cancellationToken)).Pages;
	}

	public async Task<IEnumerable<Transaction>> GetTransactionsAsync(string accountId, string from, string to, CancellationToken cancellationToken)
	{
		if (from.IsEmpty())
			throw new ArgumentNullException(nameof(from));

		Url url;

		if (to == null)
		{
			url = CreateUrl($"accounts/{accountId}/transactions/sinceid");

			url.QueryString
			   .Append("id", from);
		}
		else
		{
			url = CreateUrl($"accounts/{accountId}/transactions/idrange");

			url.QueryString
			   .Append("from", from)
			   .Append("to", to);
		}

		return await GetTransactionsAsync(url, cancellationToken);
	}

	public async Task<IEnumerable<Transaction>> GetTransactionsAsync(Uri url, CancellationToken cancellationToken)
	{
		return (await MakeRequestAsync<TransactionsResponse>(url, cancellationToken)).Transactions;
	}

	public async Task<IEnumerable<Account>> GetAccountsAsync(CancellationToken cancellationToken)
	{
		return (await MakeRequestAsync<AccountsResponse>(CreateUrl("accounts"), cancellationToken)).Accounts;
	}

	/// <summary>
	/// Gets account specific details for the given account.
	/// </summary>
	/// <param name="accountId">the ID of the account to retrieve.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>the AccountDetails for the account.</returns>
	public async Task<AccountDetails> GetAccountDetailsAsync(string accountId, CancellationToken cancellationToken)
	{
		return await MakeRequestAsync<AccountDetails>(CreateUrl($"accounts/{accountId}"), cancellationToken);
	}

	/// <summary>
	/// Get the current open positions for the account specified.
	/// </summary>
	/// <param name="accountId">the ID of the account.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>list of positions (or empty list if there are none).</returns>
	public async Task<IEnumerable<Position>> GetPositionsAsync(string accountId, CancellationToken cancellationToken)
	{
		return (await MakeRequestAsync<PositionsResponse>(CreateUrl($"accounts/{accountId}/positions"), cancellationToken)).Positions;
	}

	public async Task<IEnumerable<Candle>> GetCandlesAsync(string instrument, string timeFrame, string price, long? count, string from, string to, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"instruments/{instrument}/candles");

		url.QueryString
			.Append("granularity", timeFrame)
			.Append("price", price);

		// can raise max candle count (5k) overflow error

		if (count != null)
			url.QueryString.Append("count", count.Value);

		if (from != null)
			url.QueryString.Append("from", from);

		if (to != null)
			url.QueryString.Append("to", to);

		return (await MakeRequestAsync<CandlesResponse>(url, cancellationToken))?.Candles ?? [];
	}

	public async Task<OrderCreateResponse> CreateOrderAsync(string accountId, string instrument, decimal units, string timeInForce, string gtdTime,
		string type, string positionFill, string expiry, string price, long clientOrderId, string comment,
		decimal? lowerBound, decimal? upperBound, decimal? stopLoss, decimal? takeProfit, decimal? trailingStop, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/orders");

		var request = CreateRequest(url, Method.Post);

		request.AddHeader("ClientRequestID", clientOrderId.To<string>());

		FillOrderInfo(request, instrument, units, timeInForce, gtdTime,
			type, positionFill, expiry, price, clientOrderId, comment,
			lowerBound, upperBound, stopLoss, takeProfit, trailingStop);

		return await MakeRequestAsync<OrderCreateResponse>(request, cancellationToken);
	}

	[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
	private class OrderRequest
	{
		[JsonProperty("order")]
		public Order Order { get; set; }
	}

	private static void FillOrderInfo(RestRequest request, string instrument, decimal? units, string timeInForce,
		string gtdTime, string type, string positionFill, string expiry, string price, long clientOrderId,
		string comment, decimal? lowerBound, decimal? upperBound, decimal? stopLoss, decimal? takeProfit, decimal? trailingStop)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request.AddBodyAsStr(JsonConvert.SerializeObject(new OrderRequest
		{
			Order = new Order
			{
				Instrument = instrument,
				Units = (double)units,
				TimeInForce = timeInForce,
				Expiry = expiry,
				Type = type,
				GtdTime = gtdTime,
				PositionFill = positionFill,
				ClientExtensions = new ClientExtensions
				{
					Id = clientOrderId.To<string>(),
					Comment = comment,
				},
				Price = price,
				LowerBound = (double?)lowerBound,
				UpperBound = (double?)upperBound,
				StopLoss = (double?)stopLoss,
				TakeProfit = (double?)takeProfit,
				TrailingStop = (double?)trailingStop,
			},
		}));
	}

	public async Task<OrderCancelResponse> CancelOrderAsync(long requestId, string accountId, string orderSpecifier, CancellationToken cancellationToken)
	{
		var request = CreateRequest(CreateUrl($"accounts/{accountId}/orders/{orderSpecifier}/cancel"), Method.Put);

		request.AddHeader("ClientRequestID", requestId.To<string>());

		return await MakeRequestAsync<OrderCancelResponse>(request, cancellationToken);
	}

	public async Task<OrderReplaceResponse> ReplaceOrderAsync(long requestId, string accountId, string orderSpecifier, string instrument, decimal units, string timeInForce,
		string gtdTime, string type, string positionFill, string expiry, string price, long clientOrderId, string comment,
		decimal? lowerBound, decimal? upperBound, decimal? stopLoss, decimal? takeProfit, decimal? trailingStop, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/orders/{orderSpecifier}");

		var request = CreateRequest(url, Method.Put);

		request.AddHeader("ClientRequestID", requestId.To<string>());

		FillOrderInfo(request, instrument, units, timeInForce, gtdTime,
			type, positionFill, expiry, price, clientOrderId, comment,
			lowerBound, upperBound, stopLoss, takeProfit, trailingStop);

		return await MakeRequestAsync<OrderReplaceResponse>(request, cancellationToken);
	}

	/// <summary>
	/// Gets the list of instruments that are available.
	/// </summary>
	/// <param name="accountId">Account ID.</param>
	/// <param name="instruments">Instruments.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>a list of the available instruments.</returns>
	public async Task<IEnumerable<Instrument>> GetInstrumentsAsync(string accountId, IEnumerable<string> instruments, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"accounts/{accountId}/instruments");

		var instrumentsField = instruments.JoinComma();

		if (!instrumentsField.IsEmpty())
			url.QueryString.Append("instruments", instrumentsField);

		return (await MakeRequestAsync<InstrumentsResponse>(url, cancellationToken)).Instruments;
	}

	///// <summary>
	///// Gets the current rates for the given instruments.
	///// </summary>
	///// <param name="instruments">The list of instruments to request.</param>
	///// <returns>The list of prices.</returns>
	//public IEnumerable<Price> GetRates(IEnumerable<string> instruments)
	//{
	//	var url = CreateUrl("prices");

	//	url.QueryString
	//		.Append("instruments", instruments.JoinComma());

	//	return MakeRequest<PricesResponse>(url).Prices;
	//}

	//public IEnumerable<Calendar> GetCalendar(string instrument, int hours)
	//{
	//	var url = CreateUrl("calendar");

	//	url.QueryString
	//		.Append("instrument", instrument)
	//		.Append("period", hours);

	//	return MakeRequest<CalendarsResponse>(url).Calendars;
	//}

	private Url CreateUrl(string name)
	{
		return new Url(_restUrl + "/v3/" + name);
	}

	private RestRequest CreateRequest(Uri url, Method method = Method.Get)
	{
		if (url is null)
			throw new ArgumentNullException(nameof(url));

		var request = new RestRequest(url.PathAndQuery, method);

		// for non-sandbox requests
		if (_token != null)
			request.SetBearer(_token);

		// If "UNIX" is specified DateTime fields will be specified or returned in the "12345678.000000123" format.
		request.AddHeader("Accept-Datetime-Format", "UNIX");

		if (_useCompression)
			request.AddHeader("Accept-Encoding", "gzip");

		request.RequestFormat = DataFormat.Json;

		return request;
	}

	private Task<T> MakeRequestAsync<T>(Uri url, CancellationToken cancellationToken)
		where T : class
		=> MakeRequestAsync<T>(CreateRequest(url), cancellationToken);

	private Task<T> MakeRequestAsync<T>(RestRequest request, CancellationToken cancellationToken)
		where T : class
	{
		return Do.InvariantAsync(async () =>
		{
			return await request.InvokeAsync<T>(_restUrl.To<Uri>(), null, null, cancellationToken);
		});
	}
}
