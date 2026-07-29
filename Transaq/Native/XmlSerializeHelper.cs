namespace StockSharp.Transaq.Native;

using System.Xml.Linq;

using ConnectMessage = StockSharp.Transaq.Native.Commands.ConnectMessage;
using DisconnectMessage = StockSharp.Transaq.Native.Commands.DisconnectMessage;

static class XmlSerializeHelper
{
	private static readonly SynchronizedDictionary<string, Func<XElement, BaseResponse>> _deserializers =
		[];

	private static readonly SynchronizedDictionary<Type, Action<BaseCommandMessage, XElement>> _serializers =
		[];

	static XmlSerializeHelper()
	{
		AddSerializer<ConnectMessage>(SerializeConnect);
		AddSerializer<DisconnectMessage>(SerializeDefault);
		AddSerializer<RequestHistoryDataMessage>(SerializeRequestHistoryData);
		AddSerializer<ServerStatusMessage>(SerializeDefault);
		AddSerializer<RequestSecuritiesMessage>(SerializeDefault);
		AddSerializer<SubscribeMessage>(SerializeSubscribe);
		AddSerializer<UnsubscribeMessage>(SerializeUnsubscribe);
		AddSerializer<NewOrderMessage>(SerializeNewOrder);
		AddSerializer<NewCondOrderMessage>(SerializeNewCondOrder);
		AddSerializer<NewStopOrderMessage>(SerializeNewStopOrder);
		AddSerializer<NewRepoOrderMessage>(SerializeNewRepoOrder);
		AddSerializer<NewMRepoOrderMessage>(SerializeNewMRepoOrder);
		AddSerializer<NewRpsOrderMessage>(SerializeNewRpsOrder);
		AddSerializer<CancelOrderMessage>(SerializeCancelOrder);
		AddSerializer<CancelStopOrderMessage>(SerializeCancelOrder);
		AddSerializer<CancelNegDealMessage>(SerializeCancelOrder);
		AddSerializer<CancelReportMessage>(SerializeCancelOrder);
		AddSerializer<RequestFortsPositionsMessage>(SerializeRequestFortsPositions);
		AddSerializer<RequestClientLimitsMessage>(SerializeRequestClientLimits);
		AddSerializer<RequestMarketsMessage>(SerializeDefault);
		AddSerializer<RequestServTimeDifferenceMessage>(SerializeDefault);
		AddSerializer<RequestLeverageControlMessage>(SerializeRequestLeverageControl);
		AddSerializer<ChangePassMessage>(SerializeChangePass);
		AddSerializer<SubscribeTicksMessage>(SerializeSubscribeTicks);
		AddSerializer<RequestConnectorVersionMessage>(SerializeDefault);
		AddSerializer<RequestSecuritiesInfoMessage>(SerializeRequestSecuritiesInfo);
		AddSerializer<RequestMaxBuySellMessage>(SerializeRequestMaxBuySell);
		AddSerializer<MoveOrderMessage>(SerializeMoveOrder);
		AddSerializer<RequestServerIdMessage>(SerializeDefault);
		AddSerializer<RequestOldNewsMessage>(SerializeRequestOldNews);
		AddSerializer<RequestNewsBodyMessage>(SerializeRequestNewsBody);
		AddSerializer<RequestMcPortfolioMessage>(SerializeRequestMcPortfolio);

		_deserializers.Add("result", DeserializeBaseMessage);
		_deserializers.Add("candlekinds", DeserializeCandleKinds);
		_deserializers.Add("markets", DeserializeMarkets);
		_deserializers.Add("securities", DeserializeSecurities);
		_deserializers.Add("client", DeserializeClient);
		_deserializers.Add("positions", DeserializePositions);
		_deserializers.Add("server_status", DeserializeServerStatus);
		_deserializers.Add("overnight", DeserializeOvernight);
		_deserializers.Add("candles", DeserializeCandles);
		_deserializers.Add("error", DeserializeError);
		_deserializers.Add("connector_version", DeserializeConnectorVersion);
		_deserializers.Add("sec_info", DeserializeSecInfo);
		_deserializers.Add("sec_info_upd", DeserializeSecInfo);
		_deserializers.Add("current_server", DeserializeCurrentServer);
		_deserializers.Add("news_header", DeserializeNewsHeader);
		_deserializers.Add("news_body", DeserializeNewsBody);
		_deserializers.Add("ticks", DeserializeTicks);
		_deserializers.Add("alltrades", DeserializeAllTrades);
		_deserializers.Add("quotes", DeserializeQuotes);
		_deserializers.Add("marketord", DeserializeMarketOrd);
		_deserializers.Add("leverage_control", DeserializeLeverageControl);
		_deserializers.Add("quotations", DeserializeQuotations);
		_deserializers.Add("trades", DeserializeTrades);
		_deserializers.Add("clientlimits", DeserializeClientLimits);
		_deserializers.Add("orders", DeserializeOrders);
		_deserializers.Add("boards", DeserializeBoards);
		_deserializers.Add("pits", DeserializePits);
		_deserializers.Add("mc_portfolio", DeserializePortfolio);
		_deserializers.Add("portfolio_mct", DeserializePortfolioMct);
		_deserializers.Add("max_buy_sell", DeserializeMaxBuySell);
		_deserializers.Add("messages", DeserializeMessages);
		_deserializers.Add("union", DeserializeUnion);
		_deserializers.Add("authentication", DeserializeAuthentication);
	}

	private static double? GetDouble(this XElement elem, string name)
	{
		return elem.GetElementValue<string>(name)?.Remove("%").To<double?>();
	}

	private static void AddSerializer<T>(Action<T, XElement> handler)
		where T : BaseCommandMessage
	{
		_serializers.Add(typeof(T), (c, e) => handler((T)c, e));
	}

	private static Func<DateTime> _getNow = () => TimeHelper.Now;

	public static Func<DateTime> GetNow
	{
		get
		{
			if (_getNow == null)
				throw new InvalidOperationException();

			return _getNow;
		}
		set => _getNow = value ?? throw new ArgumentNullException(nameof(value));
	}

	public static string Serialize(BaseCommandMessage message)
	{
		var xCommand = new XElement("command");
		xCommand.Add(new XAttribute("id", message.Id));

		var type = message.GetType();

		var serializer = _serializers.TryGetValue(type) ?? throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(type));

		serializer.Invoke(message, xCommand);

		return xCommand.ToString();
	}

	public static BaseResponse Deserialize(string xmlString, out string name)
	{
		var xElement = XElement.Parse(xmlString);

		name = xElement.Name.ToString();

		var deserializer = _deserializers.TryGetValue(name) ?? throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(name));

		return deserializer.Invoke(xElement);
	}

	private static void SerializeDefault(BaseCommandMessage message, XElement rootElement)
	{
	}

	private static void SerializeConnect(ConnectMessage message, XElement rootElement)
	{
		rootElement.Add(new XElement("login", message.Login));
		rootElement.Add(new XElement("password", message.Password));
		rootElement.Add(new XElement("host", message.EndPoint.GetHost()));
		rootElement.Add(new XElement("port", message.EndPoint.GetPort()));

		if (!message.LogsDir.IsEmpty())
			rootElement.Add(new XElement("logsdir", message.LogsDir));

		if (message.LogLevel != null)
			rootElement.Add(new XElement("loglevel", (int)message.LogLevel.Value));

		rootElement.Add(new XElement("autopos", message.Autopos.ToMyString()));
		rootElement.Add(new XElement("micex_registers", message.MicexRegisters.ToMyString()));
		rootElement.Add(new XElement("milliseconds", message.Milliseconds.ToMyString()));
		rootElement.Add(new XElement("utc_time", message.Utc.ToMyString()));
		rootElement.Add(new XElement("notes_file", message.NotesFile));

		if (message.Proxy.IsEnabled)
		{
			var proxyType = string.Empty;

			switch (message.Proxy.Type)
			{
				case ProxyTypes.Http:
					proxyType = "HTTP-CONNECT";
					break;
				case ProxyTypes.Socks4:
					proxyType = "SOCKS4";
					break;
				case ProxyTypes.Socks5:
					proxyType = "SOCKS5";
					break;
			}

			var proxyElement = new XElement("proxy",
											new XAttribute("type", proxyType),
											new XAttribute("addr", message.Proxy.Address.GetHost()),
											new XAttribute("port", message.Proxy.Address.GetPort())
				);

			if (!message.Proxy.Login.IsEmpty())
			{
				proxyElement.Add(new XAttribute("login", message.Proxy.Login));
				proxyElement.Add(new XAttribute("password", message.Proxy.Password));
			}

			rootElement.Add(proxyElement);
		}

		if (message.RqDelay != null)
			rootElement.Add(new XElement("rqdelay", message.RqDelay.Value));

		if (message.SessionTimeout != null)
			rootElement.Add(new XElement("session_timeout", message.SessionTimeout.Value));

		if (message.RequestTimeout != null)
			rootElement.Add(new XElement("request_timeout", message.RequestTimeout.Value));
	}

	private static void SerializeRequestHistoryData(RequestHistoryDataMessage message, XElement rootElement)
	{
		if (SerializeSecurityId(rootElement, message.SecId, message.SecCode, message.Board))
		{
			rootElement.Add(
				new XElement("period", message.Period),
				new XElement("count", message.Count),
				new XElement("reset", message.Reset.ToMyString()));
		}
		else
		{
			rootElement.Add(
				new XAttribute("period", message.Period),
				new XAttribute("count", message.Count),
				new XAttribute("reset", message.Reset.ToMyString()));
		}
	}

	private static void SerializeSubscribe(SubscribeMessage message, XElement rootElement)
	{
		void Action(string s, Func<IEnumerable<(string secCode, string board, int nativeId)>> secids)
		{
			var ids = secids().ToArray();

			if (ids.IsEmpty())
				return;

			var el = new XElement(s);

			foreach (var (secCode, board, nativeId) in ids)
			{
				SerializeSecurityId(el, nativeId, secCode, board);
			}

			rootElement.Add(el);
		}

		Action("alltrades", () => message.AllTrades);
		Action("quotations", () => message.Quotations);
		Action("quotes", () => message.Quotes);
	}

	private static void SerializeUnsubscribe(UnsubscribeMessage message, XElement rootElement)
	{
		SerializeSubscribe(message, rootElement);
	}

	//private static void SerializeSecurityId(XElement rootElement, SecurityId id)
	//{
	//	SerializeSecurityId(rootElement, (int)id.NativeAsInt, id.SecurityCode, id.BoardCode);
	//}

	private static bool SerializeSecurityId(XElement rootElement, int secId, string secCode, string board)
	{
		if (board.IsEmpty())
		{
			if (secId == 0)
				throw new ArgumentNullException(nameof(secId));

			rootElement.Add(new XElement("secid", secId));

			return false;
		}
		else
		{
			rootElement.Add(new XElement("security",
				new XElement("seccode", secCode),
				new XElement("board", board)));

			return true;
		}
	}

	private static void SerializeSecurityId(XElement rootElement, NewBaseOrderMessage message)
	{
		SerializeSecurityId(rootElement, message.SecId, message.SecCode, message.Board);
	}

	private static void SerializeOrderAccount(NewBaseOrderMessage message, XElement rootElement)
	{
		if (message.Client.IsEmpty())
			rootElement.Add(new XElement("union", message.Union));
		else
			rootElement.Add(new XElement("client", message.Client));
	}

	private static void SerializeNewOrder(NewOrderMessage message, XElement rootElement)
	{
		SerializeSecurityId(rootElement, message);

		SerializeOrderAccount(message, rootElement);

		rootElement.Add(new XElement("price", message.Price));
		rootElement.Add(new XElement("hidden", message.Hidden));
		rootElement.Add(new XElement("quantity", message.Quantity));
		rootElement.Add(new XElement("buysell", message.BuySell));

		if (message.ByMarket)
			rootElement.Add(new XElement("bymarket"));

		rootElement.Add(new XElement("brokerref", message.BrokerRef));
		rootElement.Add(new XElement("unfilled", message.Unfilled));

		if (message.UseCredit)
			rootElement.Add(new XElement("usecredit"));

		if (message.NoSplit)
			rootElement.Add(new XElement("nosplit"));

		if (message.ExpDate != null)
			rootElement.Add(new XElement("expdate", message.ExpDate.Value.ToMyString()));
	}

	private static void SerializeNewCondOrder(NewCondOrderMessage message, XElement rootElement)
	{
		SerializeSecurityId(rootElement, message);

		SerializeOrderAccount(message, rootElement);

		rootElement.Add(new XElement("price", message.Price));
		rootElement.Add(new XElement("hidden", message.Hidden));
		rootElement.Add(new XElement("quantity", message.Quantity));
		rootElement.Add(new XElement("buysell", message.BuySell));

		if (message.ByMarket)
			rootElement.Add(new XElement("bymarket"));

		rootElement.Add(new XElement("brokerref", message.BrokerRef));
		rootElement.Add(new XElement("cond_type", message.CondType));
		rootElement.Add(new XElement("cond_value", message.CondValue));

		if (message.UseCredit)
			rootElement.Add(new XElement("usecredit"));

		if (message.NoSplit)
			rootElement.Add(new XElement("nosplit"));

		if (message.ExpDate != null)
			rootElement.Add(new XElement("expdate", message.ExpDate.Value.ToMyString()));

		if (message.ValidAfterType == TransaqAlgoOrderValidTypes.Immediately)
			rootElement.Add(new XElement("validafter", 0));
		else if (message.ValidAfterType == TransaqAlgoOrderValidTypes.Date && message.ValidAfter != null)
			rootElement.Add(new XElement("validafter", message.ValidAfter.Value.ToMyString()));

		if (message.ValidBeforeType == TransaqAlgoOrderValidTypes.Immediately)
			rootElement.Add(new XElement("validbefore", 0));
		else if (message.ValidBeforeType == TransaqAlgoOrderValidTypes.Date && message.ValidBefore != null)
			rootElement.Add(new XElement("validbefore", message.ValidBefore.Value.ToMyString()));
		else if (message.ValidBeforeType == TransaqAlgoOrderValidTypes.TillCancelled)
			rootElement.Add(new XElement("validbefore", "till_canceled"));
	}

	private static void SerializeNewStopOrder(NewStopOrderMessage message, XElement rootElement)
	{
		SerializeSecurityId(rootElement, message);

		SerializeOrderAccount(message, rootElement);

		rootElement.Add(new XElement("buysell", message.BuySell));
		rootElement.Add(new XElement("linkedorderno", message.LinkedOrderNo));

		if (message.ValidFor != null)
			rootElement.Add(new XElement("validfor", message.ValidFor.Value.ToMyString()));

		if (message.ExpDate != null)
			rootElement.Add(new XElement("expdate", message.ExpDate.Value.ToMyString()));

		void Action(NewStopOrderElement e, string name)
		{
			var element = new XElement(name);

			element.Add(new XElement("activationprice", e.ActivationPrice));

			if (e.OrderPrice != null)
				element.Add(new XElement("orderprice", e.OrderPrice)); // + (e.IsOrderPriceInPercents ? "%" : string.Empty)));

			if (e.ByMarket != null && e.ByMarket.Value)
				element.Add(new XElement("bymarket"));

			element.Add(new XElement("quantity", e.Quantity)); // + (e.IsQuantityInPercents ? "%" : string.Empty)));

			if (e.UseCredit != null)
				element.Add(new XElement("usecredit"));

			if (e.GuardTime != null)
				element.Add(new XElement("guardtime", e.GuardTime.Value));

			element.Add(new XElement("brokerref", e.BrokerRef));

			if (e.Correction != null)
				element.Add(new XElement("correction", e.Correction)); // + (e.IsCorrectionInPercents ? "%" : string.Empty)));

			if (e.Spread != null)
				element.Add(new XElement("spread", e.Spread)); // + (e.IsSpreadInPercents ? "%" : string.Empty)));

			rootElement.Add(element);
		}

		if (message.StopLoss != null)
			Action(message.StopLoss, "stoploss");

		if (message.TakeProfit != null)
			Action(message.TakeProfit, "takeprofit");
	}

	private static void SerializeNewRpsOrder(NewRpsOrderMessage message, XElement rootElement)
	{
		SerializeSecurityId(rootElement, message);

		SerializeOrderAccount(message, rootElement);

		rootElement.Add(new XElement("buysell", message.BuySell));

		rootElement.Add(new XElement("cpfirmid", message.CpFirmId));
		rootElement.Add(new XElement("matchref", message.MatchRef));
		rootElement.Add(new XElement("brokerref", message.BrokerRef));
		rootElement.Add(new XElement("price", message.Price));
		rootElement.Add(new XElement("quantity", message.Quantity));
		rootElement.Add(new XElement("settlecode", message.SettleCode));

		if (message.RefundRate != null)
			rootElement.Add(new XElement("refundrate", message.RefundRate));
	}

	private static void SerializeNewRepoOrder(NewRepoOrderMessage message, XElement rootElement)
	{
		SerializeNewRpsOrder(message, rootElement);

		if (message.Rate != null)
			rootElement.Add(new XElement("reporate", message.Rate));
	}

	private static void SerializeNewMRepoOrder(NewMRepoOrderMessage message, XElement rootElement)
	{
		SerializeNewRepoOrder(message, rootElement);

		rootElement.Add(new XElement("value", message.Value));

		if (message.Term != null)
			rootElement.Add(new XElement("repoterm", message.Term));

		if (message.StartDiscount != null)
			rootElement.Add(new XElement("startdiscount", message.StartDiscount));

		if (message.LowerDiscount != null)
			rootElement.Add(new XElement("lowerdiscount", message.LowerDiscount));

		if (message.UpperDiscount != null)
			rootElement.Add(new XElement("upperdiscount", message.UpperDiscount));

		if (message.BlockSecurities != null)
			rootElement.Add(new XElement("blocksecurities", message.BlockSecurities.Value.ToYesNo()[0]));
	}

	private static void SerializeCancelOrder(CancelOrderMessage message, XElement rootElement)
	{
		rootElement.Add(new XElement("transactionid", message.TransactionId));
	}

	private static void SerializeRequestFortsPositions(RequestFortsPositionsMessage message, XElement rootElement)
	{
		if (!message.Client.IsEmpty())
			rootElement.Add(new XAttribute("client", message.Client));
	}

	private static void SerializeRequestClientLimits(RequestClientLimitsMessage m, XElement rootElement)
	{
		SerializeRequestFortsPositions(m, rootElement);
	}

	private static void SerializeRequestLeverageControl(RequestLeverageControlMessage message, XElement rootElement)
	{
		rootElement.Add(new XAttribute("client", message.Client));

		foreach (var (code, board, id) in message.SecIds)
		{
			SerializeSecurityId(rootElement, id, code, board);
		}
	}

	private static void SerializeChangePass(ChangePassMessage message, XElement rootElement)
	{
		rootElement.Add(new XAttribute("oldpass", message.OldPass));
		rootElement.Add(new XAttribute("newpass", message.NewPass));
	}

	private static void SerializeSubscribeTicks(SubscribeTicksMessage message, XElement rootElement)
	{
		var isAttr = false;

		foreach (var item in message.Items)
		{
			if (item.Board.IsEmpty())
			{
				isAttr = true;

				rootElement.Add(
					new XElement("security",
						new XAttribute("secid", item.SecId),
						new XAttribute("tradeno", item.TradeNo)));
			}
			else
			{
				rootElement.Add(
					new XElement("security",
						new XElement("seccode", item.SecCode),
						new XElement("board", item.Board),
						new XElement("tradeno", item.TradeNo)));
			}
		}

		if (isAttr)
			rootElement.Add(new XAttribute("filter", message.Filter.ToMyString()));
		else
			rootElement.Add(new XElement("filter", message.Filter.ToMyString()));
	}

	private static void SerializeRequestSecuritiesInfo(RequestSecuritiesInfoMessage message, XElement rootElement)
	{
		rootElement.Add(
			new XElement("security",
				new XElement("market", message.Market),
				new XElement("seccode", message.SecCode)));
	}

	private static void SerializeRequestMaxBuySell(RequestMaxBuySellMessage message, XElement rootElement)
	{
		if (!message.Client.IsEmpty())
			rootElement.Add(new XAttribute("client", message.Client));

		if (!message.Union.IsEmpty())
			rootElement.Add(new XAttribute("union", message.Union));

		rootElement.Add(
			new XElement("security",
				new XElement("market", message.Market),
				new XElement("seccode", message.SecCode)));
	}

	private static void SerializeMoveOrder(MoveOrderMessage message, XElement rootElement)
	{
		rootElement.Add(
			new XElement("transactionid", message.TransactionId),
			new XElement("price", message.Price),
			new XElement("moveflag", (int)message.MoveFlag),
			new XElement("quantity", message.Quantity));
	}

	private static void SerializeRequestOldNews(RequestOldNewsMessage message, XElement rootElement)
	{
		rootElement.Add(new XAttribute("count", message.Count));
	}

	private static void SerializeRequestNewsBody(RequestNewsBodyMessage message, XElement rootElement)
	{
		rootElement.Add(new XAttribute("news_id", message.NewsId));
	}

	private static void SerializeRequestMcPortfolio(RequestMcPortfolioMessage message, XElement rootElement)
	{
		if (!message.Client.IsEmpty())
			rootElement.Add(new XAttribute("client", message.Client));

		if (!message.Union.IsEmpty())
			rootElement.Add(new XAttribute("union", message.Union));

		if (message.Currency != null)
			rootElement.Add(new XAttribute("currency", message.Currency.To<string>().ToLowerInvariant()));

		if (message.Asset != null)
			rootElement.Add(new XAttribute("asset", message.Asset.To<string>().ToLowerInvariant()));

		if (message.Money != null)
			rootElement.Add(new XAttribute("money", message.Money.To<string>().ToLowerInvariant()));

		if (message.Depo != null)
			rootElement.Add(new XAttribute("depo", message.Depo.To<string>().ToLowerInvariant()));

		if (message.Registers != null)
			rootElement.Add(new XAttribute("registers", message.Registers.To<string>().ToLowerInvariant()));

		if (message.MaxBs != null)
			rootElement.Add(new XAttribute("maxbs", message.MaxBs.To<string>().ToLowerInvariant()));
	}

	private static BaseResponse DeserializeBaseMessage(XElement rootElement)
	{
		return new BaseResponse
		{
			Text = rootElement.Value,
			IsSuccess = rootElement.GetAttributeValue("success", true),
			Diff = rootElement.GetAttributeValue<int>("diff"),
			TransactionId = rootElement.GetAttributeValue<long>("transactionid")
		};
	}

	private static BaseResponse DeserializeCandleKinds(XElement rootElement)
	{
		return new CandleKindsResponse
		{
			Kinds = rootElement
				.Descendants("kind")
				.Select(node => new CandleKind
				{
					Id = node.GetElementValue<int>("id"),
					Period = node.GetElementValue<int>("period"),
					Name = node.GetElementValue<string>("name")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeMarkets(XElement rootElement)
	{
		return new MarketsResponse
		{
			Markets = rootElement
				.Descendants("market")
				.Select(node => new Market
				{
					Id = node.GetAttributeValue<int>("id"),
					Name = node.Value
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeSecurities(XElement rootElement)
	{
		return new SecuritiesResponse
		{
			Securities = rootElement.Descendants("security").Select(node =>
			{
				var sec = new TransaqSecurity
				{
					SecId = node.GetAttributeValue<int>("secid"),
					Active = node.GetAttributeValue<bool>("active"),
					SecCode = node.GetElementValue<string>("seccode"),
					Board = node.GetElementValue<string>("board"),
					Currency = node.GetElementValue<string>("currency"),
					Market = node.GetElementValue<string>("market"),
					ShortName = node.GetElementValue<string>("shortname"),
					Decimals = node.GetElementValue<int>("decimals"),
					MinStep = node.GetDouble("minstep"),
					LotSize = node.GetElementValue<int>("lotsize"),
					PointCost = node.GetDouble("point_cost")
				};

				var opmask = node.Element("opmask");
				if (opmask != null)
				{
					sec.OpMaskUseCredit = opmask.GetAttributeValue<string>("usecredit").FromYesNo();
					sec.OpMaskByMarket = opmask.GetAttributeValue<string>("bymarket").FromYesNo();
					sec.OpMaskNoSplit = opmask.GetAttributeValue<string>("nosplit").FromYesNo();
					sec.OpMaskImmorCancel = opmask.GetAttributeValue<string>("immorcancel").FromYesNo();
					sec.OpMaskCancelBalance = opmask.GetAttributeValue<string>("cancelbalance").FromYesNo();
				}

				sec.Type = node.GetElementValue<string>("sectype");

				// http://www.transaq.ru/forum/index.php?topic=2878.0
				sec.TimeZone = node.GetElementValue("sec_tz", string.Empty);

				sec.Mic = node.GetElementValue<string>("MIC");
				sec.CurrencyId = node.GetElementValue<string>("currencyid");

				return sec;
			}).ToArray()
		};
	}

	private static BaseResponse DeserializeClient(XElement rootElement)
	{
		return new ClientResponse
		{
			Id = rootElement.GetAttributeValue<string>("id"),
			Remove = rootElement.GetAttributeValue<bool>("remove"),
			Type = rootElement.GetElementValue<ClientTypes>("type"),
			Currency = rootElement.GetElementValue<string>("currency"),
			MarketId = rootElement.GetElementValue<int>("market"),
			Union = rootElement.GetElementValue<string>("union"),
			FortsAcc = rootElement.GetElementValue<string>("forts_acc"),
			//MlIntraDay = rootElement.GetDouble("ml_intraday"),
			//MlOverNight = rootElement.GetDouble("ml_overnight"),
			//MlRestrict = rootElement.GetDouble("ml_restrict"),
			//MlCall = rootElement.GetDouble("ml_call"),
			//MlClose = rootElement.GetDouble("ml_close")
		};
	}

	private static BaseResponse DeserializePositions(XElement rootElement)
	{
		return new PositionsResponse
		{
			MoneyPositions = rootElement.Elements("money_position").Select(node => new MoneyPosition
			{
				Currency = node.GetElementValue<string>("currency"),
				Client = node.GetElementValue<string>("client"),
				Union = node.GetElementValue<string>("union"),
				Markets = node
					.Descendants("markets")
					.Select(m => new Market { Id = m.GetElementValue<int>("market") })
					.ToArray(),
				Register = node.GetElementValue<string>("register"),
				Asset = node.GetElementValue<string>("asset"),
				ShortName = node.GetElementValue<string>("shortname"),
				SaldoIn = node.GetDouble("saldoin"),
				Bought = node.GetDouble("bought"),
				Sold = node.GetDouble("sold"),
				Saldo = node.GetDouble("saldo"),
				OrdBuy = node.GetDouble("ordbuy"),
				OrdBuyCond = node.GetDouble("ordbuycond"),
				Commission = node.GetDouble("comission")
			}).ToArray(),
			SecPositions = rootElement.Elements("sec_position").Select(node => new SecPosition
			{
				Client = node.GetElementValue<string>("client"),
				Union = node.GetElementValue<string>("union"),
				SecId = node.GetElementValue<int>("secid"),
				Market = node.GetElementValue<int>("market"),
				SecCode = node.GetElementValue<string>("seccode"),
				Register = node.GetElementValue<string>("register"),
				ShortName = node.GetElementValue<string>("shortname"),
				SaldoIn = node.GetElementValueNullable<long>("saldoin"),
				SaldoMin = node.GetElementValueNullable<long>("saldomin"),
				Bought = node.GetElementValueNullable<long>("bought"),
				Sold = node.GetElementValueNullable<long>("sold"),
				Saldo = node.GetElementValueNullable<long>("saldo"),
				OrdBuy = node.GetElementValueNullable<long>("ordbuy"),
				OrdSell = node.GetElementValueNullable<long>("ordsell"),
				Amount = node.GetElementValueNullable<double>("amount"),
				Equity = node.GetElementValueNullable<double>("equity"),
			}).ToArray(),
			FortsPositions = rootElement.Elements("forts_position").Select(node => new FortsPosition
			{
				Markets = node
					.Descendants("markets")
					.Select(m => new Market { Id = m.GetElementValue<int>("market") })
					.ToArray(),
				SecId = node.GetElementValue<int>("secid"),
				SecCode = node.GetElementValue<string>("seccode"),
				Client = node.GetElementValue<string>("client"),
				Union = node.GetElementValue<string>("union"),
				StartNet = node.GetElementValueNullable<int>("startnet"),
				OpenBuys = node.GetElementValueNullable<int>("openbuys"),
				OpenSells = node.GetElementValueNullable<int>("opensells"),
				TotalNet = node.GetElementValueNullable<int>("totalnet"),
				TodayBuy = node.GetElementValueNullable<int>("todaybuy"),
				TodaySell = node.GetElementValueNullable<int>("todaysell"),
				OptMargin = node.GetDouble("optmargin"),
				VarMargin = node.GetDouble("varmargin"),
				ExpirationPos = node.GetElementValueNullable<long>("expirationpos"),
				UsedSellSpotLimit = node.GetDouble("usedsellspotlimit"),
				SellSpotLimit = node.GetDouble("sellspotlimit"),
				Netto = node.GetDouble("netto"),
				Kgo = node.GetDouble("kgo")
			}).ToArray(),
			FortsMoneys = rootElement.Elements("forts_money").Select(node => new FortsMoney
			{
				Markets = node
					.Descendants("markets")
					.Select(m => new Market { Id = m.GetElementValue<int>("market") })
					.ToArray(),
				Client = node.GetElementValue<string>("client"),
				Union = node.GetElementValue<string>("union"),
				ShortName = node.GetElementValue<string>("shortname"),
				Current = node.GetDouble("current"),
				Blocked = node.GetDouble("blocked"),
				Free = node.GetDouble("free"),
				VarMargin = node.GetDouble("varmargin")
			}).ToArray(),
			FortsCollateralses = rootElement.Elements("forts_collaterals").Select(node => new FortsCollaterals
			{
				Markets = node
					.Descendants("markets")
					.Select(m => new Market { Id = m.GetElementValue<int>("market") })
					.ToArray(),
				Client = node.GetElementValue<string>("client"),
				Union = node.GetElementValue<string>("union"),
				ShortName = node.GetElementValue<string>("shortname"),
				Current = node.GetDouble("current"),
				Blocked = node.GetDouble("blocked"),
				Free = node.GetDouble("free")
			}).ToArray(),
			SpotLimits = rootElement.Elements("spot_limit").Select(node => new SpotLimit
			{
				Markets = node
					.Descendants("markets")
					.Select(m => new Market { Id = m.GetElementValue<int>("market") })
					.ToArray(),
				Client = node.GetElementValue<string>("client"),
				ShortName = node.GetElementValue<string>("shortname"),
				BuyLimit = node.GetDouble("buylimit"),
				BuyLimitUsed = node.GetDouble("buylimitused")
			}).ToArray()
		};
	}

	private static BaseResponse DeserializeError(XElement rootElement)
	{
		return new BaseResponse
		{
			IsSuccess = false,
			Text = rootElement.Value
		};
	}

	private static BaseResponse DeserializeServerStatus(XElement rootElement)
	{
		return new ServerStatusResponse
		{
			Connected = rootElement.GetAttributeValue("connected", string.Empty),
			Recover = rootElement.GetAttributeValue("recover", string.Empty),
			TimeZone = rootElement.GetAttributeValue("server_tz", string.Empty),
			Text = rootElement.Value
		};
	}

	private static BaseResponse DeserializeOvernight(XElement rootElement)
	{
		return new OvernightResponse
		{
			Status = rootElement.GetAttributeValue<bool>("status")
		};
	}

	private static BaseResponse DeserializeCandles(XElement rootElement)
	{
		return new CandlesResponse
		{
			SecId = rootElement.GetAttributeValue<int>("secid"),
			Board = rootElement.GetAttributeValue<string>("board"),
			SecCode = rootElement.GetAttributeValue<string>("seccode"),
			Period = rootElement.GetAttributeValue<int>("period"),
			Status = (CandleResponseStatus)rootElement.GetAttributeValue<int>("status"),
			Candles = rootElement
				.Descendants("candle")
				.Select(node => new TransaqCandle
				{
					Date = node.GetAttributeValue<string>("date").ToDate(GetNow()),
					Open = node.GetAttributeValue<double>("open"),
					High = node.GetAttributeValue<double>("high"),
					Low = node.GetAttributeValue<double>("low"),
					Close = node.GetAttributeValue<double>("close"),
					Volume = node.GetAttributeValue<int>("volume"),
					Oi = node.GetAttributeValue<int>("oi")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeConnectorVersion(XElement rootElement)
	{
		return new ConnectorVersionResponse
		{
			Version = rootElement.Value
		};
	}

	private static BaseResponse DeserializeSecInfo(XElement rootElement)
	{
		return new SecInfoResponse
		{
			SecId = rootElement.GetAttributeValue<int?>("secid") ?? rootElement.GetElementValue<int>("secid"),
			SecCode = rootElement.GetElementValue<string>("seccode"),
			Market = rootElement.GetElementValue<int>("market"),
			SecName = rootElement.GetElementValue<string>("secname"),
			PName = rootElement.GetElementValue<string>("pname"),
			MatDate = rootElement.GetElementValueNullable<DateTime>("mat_date", GetNow),
			ClearingPrice = rootElement.GetDouble("clearing_price"),
			MinPrice = rootElement.GetDouble("minprice"),
			MaxPrice = rootElement.GetDouble("maxprice"),
			BuyDeposit = rootElement.GetDouble("buy_deposit"),
			SellDeposit = rootElement.GetDouble("sell_deposit"),
			BgoC = rootElement.GetDouble("bgo_c"),
			BgoNC = rootElement.GetDouble("bgo_nc"),
			BgoBuy = rootElement.GetDouble("bgo_buy"),
			Accruedint = rootElement.GetDouble("accruedint"),
			CouponValue = rootElement.GetDouble("coupon_value"),
			CouponDate = rootElement.GetElementValueNullable<DateTime>("coupon_date", GetNow),
			CouponPeriod = rootElement.GetElementValueNullable<int>("coupon_period"),
			FaceValue = rootElement.GetDouble("facevalue"),
			PutCall = rootElement.GetElementValueNullable<SecInfoPutCalls>("put_call"),
			OptType = rootElement.GetElementValueNullable<SecInfoOptTypes>("opt_type"),
			LotVolume = rootElement.GetElementValueNullable<int>("lot_volume"),
			CurrencyId = rootElement.GetElementValue<string>("currencyid"),
		};
	}

	private static BaseResponse DeserializeCurrentServer(XElement rootElement)
	{
		return new CurrentServerResponse
		{
			Id = rootElement.GetAttributeValue<int>("id")
		};
	}

	private static BaseResponse DeserializeNewsHeader(XElement rootElement)
	{
		return new NewsHeaderResponse
		{
			Id = rootElement.GetElementValue<int>("id"),
			TimeStamp = rootElement.GetElementValueNullable<DateTime>("time_stamp", GetNow),
			Text = rootElement.GetElementValue<string>("source"),
			Title = rootElement.GetElementValue<string>("title")
		};
	}

	private static BaseResponse DeserializeNewsBody(XElement rootElement)
	{
		return new NewsBodyResponse
		{
			Id = rootElement.GetElementValue<int>("id"),
			Text = rootElement.GetElementValue<string>("text")
		};
	}

	private static BaseResponse DeserializeTicks(XElement rootElement)
	{
		return new TicksResponse
		{
			Ticks = rootElement
				.Descendants("tick")
				.Select(node => new Tick
				{
					SecId = node.GetElementValue<int>("secid"),
					SecCode = node.GetElementValue<string>("seccode"),
					Board = node.GetElementValue<string>("board"),
					TradeNo = node.GetElementValue<long>("tradeno"),
					TradeTime = node.GetElementValue<string>("tradetime").ToDate(GetNow()),
					Price = node.GetElementValue<double>("price"),
					Quantity = node.GetElementValue<int>("quantity"),
					Period = node.GetElementValueNullable<TicksPeriods>("period"),
					BuySell = node.GetElementValue<BuySells>("buysell"),
					OpenInterest = node.GetElementValue<int>("openinterest")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeAllTrades(XElement rootElement)
	{
		return new AllTradesResponse
		{
			AllTrades = rootElement
				.Descendants("trade")
				.Select(node => new Tick
				{
					SecId = node.GetAttributeValue<int>("secid"),
					SecCode = node.GetElementValue<string>("seccode"),
					Board = node.GetElementValue<string>("board"),
					TradeNo = node.GetElementValue<long>("tradeno"),
					TradeTime = node.GetElementValue<string>("time").ToDate(GetNow()),
					Price = node.GetElementValue<double>("price"),
					Quantity = node.GetElementValue<int>("quantity"),
					Period = node.GetElementValueNullable<TicksPeriods>("period"),
					BuySell = node.GetElementValue<BuySells>("buysell"),
					OpenInterest = node.GetElementValue<int>("openinterest")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeQuotes(XElement rootElement)
	{
		return new QuotesResponse
		{
			Quotes = rootElement
				.Descendants("quote")
				.Select(node => new TransaqQuote
				{
					SecId = node.GetAttributeValue<int>("secid"),
					SecCode = node.GetElementValue<string>("seccode"),
					Source = node.GetElementValue<string>("source"),
					Board = node.GetElementValue<string>("board"),
					Price = node.GetElementValue<double>("price"),
					Yield = node.GetElementValue<int>("yield"),
					Buy = node.GetElementValueNullable<int>("buy"),
					Sell = node.GetElementValueNullable<int>("sell")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeMarketOrd(XElement rootElement)
	{
		return new MarketOrdResponse
		{
			SecId = rootElement.GetAttributeValue<int>("secid"),
			SecCode = rootElement.GetAttributeValue<string>("seccode"),
			Permit = rootElement.GetAttributeValue<string>("permit").FromYesNo()
		};
	}

	private static BaseResponse DeserializeLeverageControl(XElement rootElement)
	{
		return new LeverageControlResponse
		{
			Client = rootElement.GetAttributeValue<string>("client"),
			LeveragePlan = rootElement.GetDouble("leverage_plan"),
			LeverageFact = rootElement.GetDouble("leverage_fact"),
			Items = rootElement
				.Descendants("security")
				.Select(node => new LeverageControlSecurity
				{
					SecCode = node.GetAttributeValue<string>("seccode"),
					Board = node.GetAttributeValue<string>("board"),
					MaxBuy = node.GetAttributeValue<long>("maxbuy"),
					MaxSell = node.GetAttributeValue<long>("maxsell")
				})
				.ToArray(),
		};
	}


	private static BaseResponse DeserializeQuotations(XElement rootElement)
	{
		return new QuotationsResponse
		{
			Quotations = rootElement
				.Descendants("quotation")
				.Select(node => new Quotation
				{
					SecId = node.GetAttributeValue<int>("secid"),
					SecCode = node.GetElementValue<string>("seccode"),
					Board = node.GetElementValue<string>("board"),
					AccruedIntValue = node.GetDouble("accrueedintValue"),
					Open = node.GetDouble("open"),
					WAPrice = node.GetDouble("waprice"),
					BestBidVolume = node.GetElementValueNullable<int>("biddepth"),
					BidsVolume = node.GetElementValueNullable<int>("biddeptht"),
					BidsCount = node.GetElementValueNullable<int>("numbids"),
					BestAskVolume = node.GetElementValueNullable<int>("offerdepth"),
					AsksVolume = node.GetElementValueNullable<int>("offerdeptht"),
					BestBidPrice = node.GetDouble("bid"),
					BestAskPrice = node.GetDouble("offer"),
					AsksCount = node.GetElementValueNullable<int>("numoffers"),
					TradesCount = node.GetElementValueNullable<int>("numtrades"),
					VolToday = node.GetElementValueNullable<int>("voltoday"),
					OpenInterest = node.GetElementValueNullable<int>("openpositions"),
					DeltaPositions = node.GetElementValueNullable<int>("deltapositions"),
					LastTradePrice = node.GetDouble("last"),
					LastTradeVolume = node.GetElementValueNullable<int>("quantity"),
					LastTradeTime = node.GetElementValueNullable<DateTime>("time", GetNow),
					Change = node.GetDouble("change"),
					PriceMinusPrevWAPrice = node.GetDouble("priceminusprevwaprice"),
					ValToday = node.GetDouble("valtoday"),
					Yield = node.GetDouble("yield"),
					YieldAtWAPrice = node.GetDouble("yieldatwaprice"),
					MarketPriceToday = node.GetDouble("marketpricetoday"),
					HighBid = node.GetDouble("highbid"),
					LowAsk = node.GetDouble("lowoffer"),
					High = node.GetDouble("high"),
					Low = node.GetDouble("low"),
					ClosePrice = node.GetDouble("closeprice"),
					CloseYield = node.GetDouble("closeyield"),
					Status = node.GetElementValueNullable<TransaqSecurityStatus>("status"),
					SessionStatus = node.GetElementValue<string>("status"),
					BuyDeposit = node.GetDouble("buydeposit"),
					SellDeposit = node.GetDouble("selldeposit"),
					Volatility = node.GetDouble("volatility"),
					TheoreticalPrice = node.GetDouble("theoreticalprice"),
					BgoBuy = node.GetDouble("bgo_buy"),
					PointCost = node.GetDouble("point_cost"),
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeTrades(XElement rootElement)
	{
		return new TradesResponse
		{
			Trades = rootElement
				.Descendants("trade")
				.Select(node => new TransaqMyTrade
				{
					SecId = node.GetElementValue<int>("secid"),
					SecCode = node.GetElementValue<string>("seccode"),
					TradeNo = node.GetElementValue<long>("tradeno"),
					OrderNo = node.GetElementValue<long>("orderno"),
					Board = node.GetElementValue<string>("board"),
					Client = node.GetElementValue<string>("client"),
					Union = node.GetElementValue<string>("union"),
					BuySell = node.GetElementValue<BuySells>("buysell"),
					Time = node.GetElementValue<string>("time").ToDate(GetNow()),
					BrokerRef = node.GetElementValue<string>("brokerref"),
					Value = node.GetDouble("value"),
					Commission = node.GetDouble("commission"),
					Price = node.GetElementValue<double>("price"),
					Quantity = node.GetElementValue<int>("quantity"),
					Yield = node.GetDouble("yield"),
					AccrueEdint = node.GetDouble("accrueedint"),
					TradeType = node.GetElementValue<TradeTypes>("tradetype"),
					SettleCode = node.GetElementValue<string>("settlecode"),
					CurrentPos = node.GetElementValue<long>("currentpos")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeClientLimits(XElement rootElement)
	{
		return new ClientLimitsResponse
		{
			Client = rootElement.GetAttributeValue<string>("client"),
			CBPLimit = rootElement.GetDouble("cbplimit"),
			CBPlused = rootElement.GetDouble("cbplused"),
			CBPLPlanned = rootElement.GetDouble("cbplplanned"),
			FobVarMargin = rootElement.GetDouble("fob_varmargin"),
			Coverage = rootElement.GetDouble("coverage"),
			LiquidityC = rootElement.GetDouble("liquidity_c"),
			Profit = rootElement.GetDouble("profit"),
			MoneyCurrent = rootElement.GetDouble("money_current"),
			MoneyBlocked = rootElement.GetDouble("money_blocked"),
			MoneyFree = rootElement.GetDouble("money_free"),
			OptionsPremium = rootElement.GetDouble("options_premium"),
			ExchangeFee = rootElement.GetDouble("exchange_fee"),
			FortsVarMargin = rootElement.GetDouble("forts_varmargin"),
			VarMargin = rootElement.GetDouble("varmargin"),
			PclMargin = rootElement.GetDouble("pclmargin"),
			OptionsVm = rootElement.GetDouble("options_vm"),
			SpotBuyLimit = rootElement.GetDouble("spot_buy_limit"),
			UsedStopBuyLimit = rootElement.GetDouble("used_stop_buy_limit"),
			CollatCurrent = rootElement.GetDouble("collat_current"),
			CollatBlocked = rootElement.GetDouble("collat_blocked"),
			CollatFree = rootElement.GetDouble("collat_free")
		};
	}

	private static BaseResponse DeserializeOrders(XElement rootElement)
	{
		return new OrdersResponse
		{
			Orders = rootElement
				.Descendants("order")
				.Select(orderElement => new TransaqOrder
				{
					TransactionId = orderElement.GetAttributeValue<int>("transactionid"),
					OrderNo = orderElement.GetElementValue<long>("orderno"),
					Board = orderElement.GetElementValue<string>("board"),
					SecId = orderElement.GetElementValue<int>("secid"),
					SecCode = orderElement.GetElementValue<string>("seccode"),
					Client = orderElement.GetElementValue<string>("client"),
					Union = orderElement.GetElementValue<string>("union"),
					Status = orderElement.GetElementValue<TransaqOrderStatus>("status"),
					BuySell = orderElement.GetElementValue<BuySells>("buysell"),
					Time = orderElement.GetElementValueNullable<DateTime>("time", GetNow),
					ExpDate = orderElement.GetElementValueNullable<DateTime>("expdate", GetNow),
					OriginOrderNo = orderElement.GetElementValueNullable<long>("origin_orderno"),
					AcceptTime = orderElement.GetElementValueNullable<DateTime>("accepttime", GetNow),
					BrokerRef = orderElement.GetElementValue<string>("brokerref"),
					Value = orderElement.GetDouble("value"),
					AccruEdint = orderElement.GetDouble("accruedint"),
					SettleCode = orderElement.GetElementValue<string>("settlecode"),
					Balance = orderElement.GetElementValue<int>("balance"),
					Price = orderElement.GetDouble("price"),
					Quantity = orderElement.GetElementValue<int>("quantity"),
					Hidden = orderElement.GetElementValue<int>("hidden"),
					Yield = orderElement.GetDouble("yield"),
					WithdrawTime = orderElement.GetElementValueNullable<DateTime>("withdrawtime", GetNow),
					ConditionType = orderElement.GetElementValue<TransaqAlgoOrderConditionTypes>("condition"),
					ConditionValue = orderElement.GetDouble("conditionvalue"),
					ValidAfter = orderElement.GetElementValueNullable<DateTime>("validafter", GetNow),
					ValidBefore = orderElement.GetElementValueNullable<DateTime>("validbefore", GetNow),
					MaxCommission = orderElement.GetDouble("maxcomission"),
					Result = orderElement.GetElementValue<string>("result")
				})
				.ToArray(),

			StopOrders = rootElement
				.Descendants("stoporder")
				.Select(sOrderElement =>
				{
					var stopOrder = new TransaqStopOrder
					{
						TransactionId = sOrderElement.GetAttributeValue<int>("transactionid"),
						ActiveOrderNo = sOrderElement.GetElementValueNullable<long>("activeorderno"),
						Board = sOrderElement.GetElementValue<string>("board"),
						SecCode = sOrderElement.GetElementValue<string>("seccode"),
						Client = sOrderElement.GetElementValue<string>("client"),
						Union = sOrderElement.GetElementValue<string>("union"),
						BuySell = sOrderElement.GetElementValue<BuySells>("buysell"),
						Canceller = sOrderElement.GetElementValue<string>("canceller"),
						AllTradeNo = sOrderElement.GetElementValueNullable<long>("alltradeno"),
						ValidBefore = sOrderElement.GetElementValueNullable<DateTime>("validbefore", GetNow),
						Author = sOrderElement.GetElementValue<string>("author"),
						AcceptTime = sOrderElement.GetElementValue<string>("accepttime").ToDate(GetNow()),
						LinkedOrderNo = sOrderElement.GetElementValueNullable<long>("linkedorderno"),
						ExpDate = sOrderElement.GetElementValueNullable<DateTime>("expdate", GetNow),
						Status = sOrderElement.GetElementValue<TransaqOrderStatus>("status")
					};

					var stopLossElement = sOrderElement.Element("stoploss");
					if (stopLossElement != null)
					{
						var stopLoss = new StopLoss
						{
							UseCredit = stopLossElement.GetElementValue<string>("usecredit").FromYesNo(),
							ActivationPrice = stopLossElement.GetDouble("activationprice"),
							GuardTime = stopLossElement.GetElementValueNullable<DateTime>("guardtime", GetNow),
							BrokerRef = stopLossElement.GetElementValue<string>("brokerref"),
							Quantity = stopLossElement.GetDouble("quantity"),
							OrderPrice = stopLossElement.GetDouble("orderprice")
						};

						stopOrder.StopLoss = stopLoss;
					}

					var takeProfitElement = sOrderElement.Element("takeprofit");
					if (takeProfitElement != null)
					{
						var takeProfit = new TakeProfit
						{
							ActivationPrice = takeProfitElement.GetDouble("activationprice"),
							GuardTime = takeProfitElement.GetElementValueNullable<DateTime>("guardtime", GetNow),
							BrokerRef = takeProfitElement.GetElementValue<string>("brokerref"),
							Quantity = takeProfitElement.GetDouble("quantity"),
							Extremum = takeProfitElement.GetDouble("extremum"),
							Level = takeProfitElement.GetDouble("level"),
							Correction = takeProfitElement.GetElementValueToUnit("correction"),
							GuardSpread = takeProfitElement.GetElementValueToUnit("guardspread")
						};

						stopOrder.TakeProfit = takeProfit;
					}

					return stopOrder;
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeBoards(XElement rootElement)
	{
		return new BoardsResponse
		{
			Boards = rootElement
				.Descendants("board")
				.Select(node => new Board
				{
					Id = node.GetAttributeValue<string>("id"),
					Name = node.GetElementValue<string>("name"),
					Market = node.GetElementValue<int>("market"),
					Type = node.GetElementValue<int>("type"),
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializePits(XElement rootElement)
	{
		return new PitsResponse
		{
			Pits = rootElement
				.Descendants("pit")
				.Select(node => new Pit
				{
					SecCode = node.GetAttributeValue<string>("seccode"),
					Board = node.GetAttributeValue<string>("board"),
					Market = node.GetElementValue<string>("market"),
					Decimals = node.GetElementValue<int>("decimals"),
					MinStep = node.GetDouble("minstep"),
					LotSize = node.GetElementValue<int>("lotsize"),
					PointCost = node.GetDouble("point_cost"),
					CurrencyId = node.GetElementValue<string>("currencyid"),
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializePortfolio(XElement rootElement)
	{
		var result = new PortfolioResponse
		{
			Union = rootElement.GetAttributeValue<string>("union"),
			Client = rootElement.GetAttributeValue<string>("client"),

			OpenEquity = rootElement.GetDouble("open_equity"),
			Equity = rootElement.GetDouble("equity"),
			PnL = rootElement.GetDouble("pl"),
			Margin = rootElement.GetDouble("go"),
			Cover = rootElement.GetDouble("cover"),
			ReqInit = rootElement.GetDouble("init_req"),
			ReqMaint = rootElement.GetDouble("maint_req"),
			PnLUnreal = rootElement.GetDouble("unrealized_pnl"),

			Assets = rootElement
				.Descendants("asset")
				.Select(node => new Asset
				{
					Code = node.GetAttributeValue<string>("code"),
					Name = node.GetAttributeValue<string>("name"),

					Currency = node.GetElementValue<string>("currency"),
					OpenBalance = node.GetDouble("open_balance"),
					Bought = node.GetDouble("bought"),
					Sold = node.GetDouble("sold"),
					Balance = node.GetDouble("balance"),
					Blocked = node.GetDouble("blocked"),
					Estimated = node.GetDouble("estimated"),
					SetoffRate = node.GetDouble("setoff_rate"),
					ReqInit = node.GetDouble("init_req"),
					ReqMaint = node.GetDouble("maint_req"),
				})
				.ToArray(),

			Money = rootElement
				.Descendants("money")
				.Select(node => new Money
				{
					Name = node.GetAttributeValue<string>("name"),
					Currency = node.GetAttributeValue<string>("currency"),

					Asset = node.GetElementValue<string>("asset"),
					OpenBalance = node.GetDouble("open_balance"),
					Bought = node.GetDouble("bought"),
					Sold = node.GetDouble("sold"),
					Balance = node.GetDouble("balance"),
					Blocked = node.GetDouble("blocked"),
					Estimated = node.GetDouble("estimated"),
					Fee = node.GetDouble("fee"),
					VarMargin = node.GetDouble("vm"),
					FinRes = node.GetDouble("finres"),
					Cover = node.GetDouble("cover"),
					ValueParts = node
						.Descendants("value_part")
						.Select(n => new MoneyValuePart
						{
							Register = n.GetAttributeValue<string>("register"),
							OpenBalance = n.GetDouble("open_balance"),
							Bought = n.GetDouble("bought"),
							Sold = n.GetDouble("sold"),
							Settled = n.GetDouble("settled"),
							Balance = n.GetDouble("balance"),
						})
						.ToArray()
				})
				.ToArray(),

			Securities = rootElement
				.Descendants("security")
				.Select(node => new TPlusSecurity
				{
					SecId = node.GetAttributeValue<int>("secid"),
					Market = node.GetElementValue<int>("market"),
					SecCode = node.GetElementValue<string>("seccode"),
					Price = node.GetDouble("price"),
					OpenBalance = node.GetElementValue<int>("open_balance"),
					Bought = node.GetElementValue<int>("bought"),
					Sold = node.GetElementValue<int>("sold"),
					Balance = node.GetElementValue<int>("balance"),
					BalancePrc = node.GetDouble("balance_prc"),
					UnrealizedPnL = node.GetDouble("unrealized_pnl"),
					Buying = node.GetElementValue<int>("buying"),
					Selling = node.GetElementValue<int>("selling"),
					Cover = node.GetDouble("cover"),
					InitMargin = node.GetDouble("init_margin"),
					PnLIncome = node.GetDouble("pnl_income"),
					PnLIntraday = node.GetDouble("pnl_intraday"),
					RiskRateLong = node.GetDouble("riskrate_long"),
					RiskRateShort = node.GetDouble("riskrate_short"),
					MaxBuy = node.GetElementValue<int>("maxbuy"),
					MaxSell = node.GetElementValue<int>("maxsell"),
					ValueParts = node
						.Descendants("value_part")
						.Select(n => new TPlusSecurityValuePart
						{
							Register = n.GetAttributeValue<string>("register"),

							OpenBalance = n.GetElementValue<int>("open_balance"),
							Bought = n.GetElementValue<int>("bought"),
							Sold = n.GetElementValue<int>("sold"),
							Settled = n.GetElementValue<int>("settled"),
							Balance = n.GetElementValue<int>("balance"),
							Buying = n.GetElementValue<int>("buying"),
							Selling = n.GetElementValue<int>("selling"),
						})
						.ToArray()
				})
				.ToArray()
		};

		return result;
	}

	private static BaseResponse DeserializePortfolioMct(XElement rootElement)
	{
		return new PortfolioMctResponse
		{
			Client = rootElement.GetAttributeValue<string>("client"),
			Capital = rootElement.GetDouble("capital"),
			UtilizationFact = rootElement.GetDouble("utilization_fact"),
			UtilizationPlan = rootElement.GetDouble("utilization_plan"),
			CoverageFact = rootElement.GetDouble("coverage_fact"),
			CoveragePlan = rootElement.GetDouble("coverage_plan"),
			OpenBalance = rootElement.GetDouble("open_balance"),
			Tax = rootElement.GetDouble("tax"),
			PnLIncome = rootElement.GetDouble("pnl_income"),
			PnLIntraday = rootElement.GetDouble("pnl_intraday"),

			Securities = rootElement
				.Descendants("security")
				.Select(node => new MctSecurity
				{
					SecId = node.GetAttributeValue<string>("secid"),
					Market = node.GetElementValue<int>("market"),
					SecCode = node.GetElementValue<string>("seccode"),
					GoRate = node.GetDouble("go_rate"),
					GoRateLong = node.GetDouble("go_rate_long"),
					GoRateShort = node.GetDouble("go_rate_short"),
					Price = node.GetDouble("price"),
					InitRate = node.GetDouble("init_rate"),
					CrossRate = node.GetDouble("cross_rate"),
					InitCrossRate = node.GetDouble("init_cross_rate"),
					OpenBalance = node.GetElementValue<int>("open_balance"),
					Bought = node.GetElementValue<int>("bought"),
					Sold = node.GetElementValue<int>("sold"),
					Balance = node.GetElementValue<int>("balance"),
					Buying = node.GetElementValue<int>("buying"),
					Selling = node.GetElementValue<int>("selling"),
					PosCost = node.GetDouble("pos_cost"),
					GoPosFact = node.GetDouble("go_pos_fact"),
					GoPosPlan = node.GetDouble("go_pos_plan"),
					Tax = rootElement.GetDouble("tax"),
					PnLIncome = node.GetDouble("pnl_income"),
					PnLIntraday = node.GetDouble("pnl_intraday"),
					MaxBuy = node.GetElementValue<long>("maxbuy"),
					MaxSell = node.GetElementValue<long>("maxsell"),
					BoughtAverage = node.GetDouble("bought_average"),
					SoldAverage = node.GetDouble("sold_average"),
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeMaxBuySell(XElement rootElement)
	{
		return new MaxBuySellResponse
		{
			Client = rootElement.GetAttributeValue<string>("client"),
			Union = rootElement.GetAttributeValue<string>("union"),

			Securities = rootElement
				.Descendants("security")
				.Select(node => new MaxBuySellSecurity
				{
					SecId = node.GetAttributeValue<string>("secid"),
					Market = node.GetElementValue<int>("market"),
					SecCode = node.GetElementValue<string>("seccode"),
					MaxBuy = node.GetElementValue<long>("maxbuy"),
					MaxSell = node.GetElementValue<long>("maxsell"),
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeMessages(XElement rootElement)
	{
		return new MessagesResponse
		{
			Messages = rootElement
				.Descendants("message")
				.Select(node => new TransaqMessage
				{
					Date = node.GetElementValueNullable<DateTime>("date", GetNow),
					Urgent = node.GetElementValue<string>("urgent").FromYesNo(),
					From = node.GetElementValue<string>("from"),
					Text = node.GetElementValue<string>("text")
				})
				.ToArray()
		};
	}

	private static BaseResponse DeserializeUnion(XElement rootElement)
	{
		return new UnionResponse
		{
			Id = rootElement.GetAttributeValue<string>("id"),
			Remove = rootElement.GetAttributeValue<bool>("remove"),
		};
	}

	private static BaseResponse DeserializeAuthentication(XElement rootElement)
	{
		return new AuthenticationResponse
		{
			Content = rootElement.ToString(),
		};
	}
}
