namespace StockSharp.Fix.Dialects;

/// <summary>
/// Sova Capital FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.sovacapital)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SovaCapitalKey,
	GroupName = LocalizedStrings.StockKey)]
public class SovaCapitalFixDialect : BaseFixDialect
{
	private static class OtkritieTags
	{
		public const FixTags TradeNum = (FixTags)5001;
		public const FixTags OrderNum = (FixTags)5002;
		public const FixTags DisplayQty = (FixTags)1138;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SovaCapitalFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public SovaCapitalFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix42)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.ffffff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		DateParser = new FastDateTimeParser("yyyyMMdd");
	}

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => Messages.Extensions.AnyDepths;

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		//MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		//MessageTypes.OrderGroupCancel.ToInfo(),
		//MessageTypes.OrderStatus.ToInfo(),

		//MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_SOVACAPITAL";
#endif

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> new DataType[]
		{
			DataType.MarketDepth,
			DataType.Level1,
		}.ToAsyncEnumerable();

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			{
				var regMsg = (OrderRegisterMessage)message;

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

				var securityId = regMsg.SecurityId;

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await WriteCurrencyAsync(writer, regMsg, cancellationToken);

				var tif = regMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);
				}

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(OtkritieTags.DisplayQty, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;
				var securityId = cancelMsg.SecurityId;

				await WriteAccountAsync(writer, cancelMsg, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;
				var securityId = replaceMsg.SecurityId;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(replaceMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				if (replaceMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(OtkritieTags.DisplayQty, cancellationToken);
					await writer.WriteAsync(replaceMsg.VisibleVolume.Value, cancellationToken);
				}

				await WriteCurrencyAsync(writer, replaceMsg, cancellationToken);

				var tif = replaceMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (!replaceMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(replaceMsg.ClientCode, cancellationToken);
				}

				return FixMessages.OrderCancelReplaceRequest;
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
				await writer.WriteAsync(mdMsg.MaxDepth ?? 0, cancellationToken);

				await writer.WriteAsync(FixTags.MDUpdateType, cancellationToken);
				await writer.WriteAsync((int)MDUpdateType.FullRefresh, cancellationToken);

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.SecurityExchange, cancellationToken);
				await writer.WriteAsync(securityId.BoardCode, cancellationToken);

				await WriteCurrencyAsync(writer, mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				if (mdMsg.DataType2 == DataType.Level1)
				{
					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.Trade, cancellationToken);

					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.ClosingPrice, cancellationToken);
				}
				else // if (mdMsg.DataType2 == DataType.MarketDepth)
				{
					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.Bid, cancellationToken);

					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.Offer, cancellationToken);
				}

				return FixMessages.MarketDataRequest;
			}

			case MessageTypes.SecurityLookup:
			{
				var lookupMsg = (SecurityLookupMessage)message;

				await writer.WriteAsync(FixTags.SecurityReqID, cancellationToken);
				await writer.WriteAsync(lookupMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.SecurityRequestType, cancellationToken);
				await writer.WriteAsync((int)SecurityRequestType.RequestListSecurities, cancellationToken);

				return FixMessages.SecurityDefinitionRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteCurrencyAsync(IFixWriter writer, SecurityMessage message, CancellationToken cancellationToken)
	{
		if (message.Currency == null)
			return;

		await writer.WriteAsync(FixTags.Currency, cancellationToken);
		await writer.WriteAsync(message.Currency.Value.ToMicexCurrencyName(), cancellationToken);
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityId securityId, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

		if (securityId.Isin.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.ExchangeSymbol, cancellationToken);
		}
		else if (!securityId.Isin.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.Isin, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.IsinNumber, cancellationToken);
		}

		await writer.WriteAsync(FixTags.ExDestination, cancellationToken);
		await writer.WriteAsync(securityId.BoardCode, cancellationToken);
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.ToLowerInvariant().EqualsIgnoreCase("replying to logout");
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case FixMessages.ExecutionReport:
			{
				long? tradeNum = null;
				long? orderNum = null;

				var report = new ExecutionReport();

				var isOk = await ReadExecutionReportAsync(reader, report, TimeStampParser, async (tag, r1, r2, cancellationToken) =>
				{
					switch (tag)
					{
						case OtkritieTags.TradeNum:
							tradeNum = await reader.ReadLongAsync(cancellationToken);
							return true;
						case OtkritieTags.OrderNum:
							orderNum = await reader.ReadLongAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				async IAsyncEnumerable<ExecutionMessage> processReports(ExecutionReport report, ExecutionMessage execMsg, [EnumeratorCancellation]CancellationToken cancellationToken)
				{
					await Task.Yield();

					if (report.ExecType == ExecType.PendingReplace || report.ExecType == ExecType.Replaced)
					{
						execMsg.OrderId = tradeNum;

						if (report.ExecType == ExecType.Replaced)
						{
							yield return new ExecutionMessage
							{
								DataTypeEx = DataType.Transactions,
								ServerTime = execMsg.ServerTime,
								OrderId = orderNum,
								OrderState = OrderStates.Done,
								Balance = report.LeavesQty,
								OriginalTransactionId = report.OrigClOrdId.To<long>(),
								HasOrderInfo = true,
							};
						}
					}
					else
					{
						execMsg.TradeId = tradeNum;
						execMsg.OrderId = orderNum;
					}

					yield return execMsg;
				}

				var result = ProcessExecutionReportAsync(report, processReports, cancellationToken);

				await foreach (var msg in result)
					yield return msg;

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