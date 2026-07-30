namespace StockSharp.Fix.Dialects;

/// <summary>
/// DukasCopy FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.dukascopy)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DukasCopyKey,
	GroupName = LocalizedStrings.ForexKey)]
public class DukasCopyFixDialect : BaseFixDialect
{
	private static class DukasTags
	{
		public const FixTags NotifPriority = (FixTags)7003;
		public const FixTags AccountName = (FixTags)7004;
		public const FixTags Leverage = (FixTags)7005;
		public const FixTags UsableMargin = (FixTags)7006;
		public const FixTags Equity = (FixTags)7007;
		public const FixTags Amount = (FixTags)7008;
		public const FixTags Slippage = (FixTags)7011;
	}

	private static class DukasMessages
	{
		public const string Notification = "U1";
		public const string AccountInfo = "U2";
		public const string PositionInfo = "U3";
		public const string OvernightReport = "U4";
		public const string ActivationRequest = "U5";
		public const string ActivationResponse = "U6";
		public const string AccountInfoRequest = "U7";
	}

	private readonly SynchronizedSet<long> _sentTransactions = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="DukasCopyFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public DukasCopyFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		//HasPosition = false;

		//PortfolioLookup = true;
		//OrderLookup = true;
		
		ExchangeBoard = BoardCodes.DukasCopy;
	}

	private const int _maxDepth = 5;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [.. Enumerable.Range(1, _maxDepth)];

	/// <inheritdoc />
	protected override bool LoginAsPortfolioName => true;

	/// <inheritdoc />
	protected override void OnReset()
	{
		base.OnReset();
		_sentTransactions.Clear();
	}

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		//MessageTypes.SecurityLookup.ToInfo(),

		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		//MessageTypes.OrderGroupCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		//MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> new DataType[]
		{
			DataType.MarketDepth,
			DataType.Level1,
		}.ToAsyncEnumerable();

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_DUKASCOPY";
#endif

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			{
				var regMsg = (OrderRegisterMessage)message;

				_sentTransactions.Add(regMsg.TransactionId);

				switch (regMsg.OrderType)
				{
					case null:
					case OrderTypes.Limit:
					case OrderTypes.Market:
					case OrderTypes.Conditional:
						break;
					default:
						throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				await WriteSecurityIdAsync(writer, regMsg.SecurityId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteExpiryDateAsync(regMsg, DateParser, TimeZone, cancellationToken);

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (regMsg.Slippage != null)
				{
					await writer.WriteAsync(DukasTags.Slippage, cancellationToken);
					await writer.WriteAsync(regMsg.Slippage.Value, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				_sentTransactions.Add(cancelMsg.TransactionId);

				if (cancelMsg.OrderId == null)
					throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

				await writer.WriteAsync(FixTags.OrderID, cancellationToken);
				await writer.WriteAsync(cancelMsg.OrderId.Value, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, cancelMsg, cancellationToken);

				await WriteSecurityIdAsync(writer, cancelMsg.SecurityId, cancellationToken);

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				_sentTransactions.Add(replaceMsg.TransactionId);

				if (replaceMsg.OldOrderId == null)
					throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

				await writer.WriteAsync(FixTags.OrderID, cancellationToken);
				await writer.WriteAsync(replaceMsg.OldOrderId.Value, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.Price, cancellationToken);
				await writer.WriteAsync(replaceMsg.Price, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteExpiryDateAsync(replaceMsg, DateParser, TimeZone, cancellationToken);

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				if (replaceMsg.Slippage != null)
				{
					await writer.WriteAsync(DukasTags.Slippage, cancellationToken);
					await writer.WriteAsync(replaceMsg.Slippage.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, replaceMsg.SecurityId, cancellationToken);

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixTimeInForce(), cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				await writer.WriteAsync(FixTags.MassStatusReqType, cancellationToken);
				await writer.WriteAsync((int)MassStatusReqType.StatusForAllOrders, cancellationToken);

				await writer.WriteAsync(FixTags.MassStatusReqID, cancellationToken);
				await writer.WriteAsync(statusMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, statusMsg, cancellationToken);

				return FixMessages.OrderMassStatusRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var pfMsg = (PortfolioLookupMessage)message;

				if (!pfMsg.IsSubscribe)
					return null;

				await WriteAccountAsync(writer, pfMsg, cancellationToken);

				return DukasMessages.AccountInfoRequest;
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;
				var securityId = mdMsg.SecurityId;

				if (mdMsg.DataType2 == DataType.Level1 ||
					mdMsg.DataType2 == DataType.MarketDepth)
				{
				}
				else
				{
					return null;
				}

				await writer.WriteSubscriptionAsync(mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.MarketDepth, cancellationToken);
				await writer.WriteAsync(mdMsg.MaxDepth ?? _maxDepth, cancellationToken);

				await writer.WriteAsync(FixTags.MDUpdateType, cancellationToken);
				await writer.WriteAsync((int)MDUpdateType.FullRefresh, cancellationToken);

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Bid, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Offer, cancellationToken);

				return FixMessages.MarketDataRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityId securityId, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.EqualsIgnoreCase("replying to logout");
	}

	/// <inheritdoc />
	protected override OrderTypes GetOrderType(ExecutionReport report, out OrderCondition condition)
	{
		condition = null;

		return report.OrdType switch
		{
			OrdType.StopLimit => OrderTypes.Limit,
			OrdType.CounterOrderSelection => report.Price == null ? OrderTypes.Market : OrderTypes.Limit,
			_ => base.GetOrderType(report, out condition),
		};
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case FixMessages.ExecutionReport:
			{
				decimal? slippage = null;

				var report = new ExecutionReport();

				var isOk = await ReadExecutionReportAsync(reader, report, TimeStampParser, async (tag, r1, r2, cancellationToken) =>
				{
					switch (tag)
					{
						case DukasTags.Slippage:
							slippage = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				report.Account ??= Login;

				if (report.Price == null && report.CumQty == 0 && report.LeavesQty > 0)
				{
					report.Price = report.AvgPx;
					report.AvgPx = null;
				}

				// '2' Filled (if partly too)
				if (report.OrdStatus == OrdStatus.Filled)
				{
					if (report.LeavesQty > 0)
						report.OrdStatus = OrdStatus.PartiallyFilled;
				}

				if (!report.ExecId.IsEmpty() && report.CumQty > 0 && report.LeavesQty != null)
				{
					report.LastPx = report.AvgPx;
					
					if (report.LeavesQty == 0)
						report.ExecType = ExecType.Fill;
					else if (report.LeavesQty > 0)
						report.ExecType = ExecType.PartialFill;
				}

				async IAsyncEnumerable<ExecutionMessage> processReports(ExecutionReport report, ExecutionMessage execMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
				{
					await Task.Yield();

					execMsg.Slippage = slippage;

					if (execMsg.OriginalTransactionId != 0 && _sentTransactions.TryAdd(execMsg.OriginalTransactionId))
					{
						execMsg.TransactionId = execMsg.OriginalTransactionId;
						execMsg.OriginalTransactionId = 0;
					}

					yield return execMsg;
				}

				var result = ProcessExecutionReportAsync(report, processReports, cancellationToken);

				await foreach (var msg in result)
					yield return msg;

				break;
			}

			case DukasMessages.Notification:
			{
				string account = null;
				string accountName = null;
				string text = null;
				int? priority = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case DukasTags.AccountName:
							accountName = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.Text:
							text = await reader.ReadStringAsync(cancellationToken);
							return true;
						case DukasTags.NotifPriority:
							priority = await reader.ReadIntAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new NewsMessage
				{
					Story = text,
					Source = account ?? Login,
				};

				break;
			}

			case DukasMessages.AccountInfo:
			{
				decimal? leverage = null;
				decimal? usableMargin = null;
				decimal? equity = null;
				string currency = null;
				string accountName = null;
				string account = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case DukasTags.Leverage:
							leverage = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case DukasTags.UsableMargin:
							usableMargin = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case DukasTags.Equity:
							equity = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case DukasTags.AccountName:
							accountName = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.Currency:
							currency = await reader.ReadStringAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					PortfolioName = account ?? Login,
				}
				.TryAdd(PositionChangeTypes.Currency, currency.FromMicexCurrencyName(this.AddErrorLog))
				.TryAdd(PositionChangeTypes.Leverage, leverage, true)
				.TryAdd(PositionChangeTypes.BlockedValue, usableMargin, true)
				.TryAdd(PositionChangeTypes.CurrentValue, equity, true);

				break;
			}

			case DukasMessages.PositionInfo:
			{
				string symbol = null;
				string account = null;
				string accountName = null;
				decimal? amount = null;
				decimal? price = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case FixTags.Symbol:
							symbol = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case DukasTags.AccountName:
							accountName = await reader.ReadStringAsync(cancellationToken);
							return true;
						case DukasTags.Amount:
							amount = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.Price:
							price = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new PositionChangeMessage
				{
					SecurityId = new SecurityId
					{
						SecurityCode = symbol,
						BoardCode = ExchangeBoard
					},
					PortfolioName = account ?? Login,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, amount, true)
				.TryAdd(PositionChangeTypes.AveragePrice, price);

				break;
			}

			case DukasMessages.OvernightReport:
			{
				break;
			}

			case DukasMessages.ActivationResponse:
			{
				break;
			}

			default:
			{
				await foreach (var msg in base.OnReadAsync(reader, msgType, cancellationToken))
					yield return msg;

				break;
			}
		}
	}
}