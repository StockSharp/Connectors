namespace StockSharp.Fix.Dialects;

/// <summary>
/// BRVM FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.brvm)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BrvmKey,
	GroupName = LocalizedStrings.StockKey)]
public class BrvmFixDialect : BaseFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BrvmFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BrvmFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		//OrderCancelVolumeRequired = OrderCancelVolumeRequireTypes.Volume;
	}

	private const int _maxDepth = 5;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [.. Enumerable.Range(1, _maxDepth)];

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderGroupCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> new DataType[]
		{
			DataType.MarketDepth,
			DataType.Level1,
			DataType.Ticks,
		}.ToAsyncEnumerable();

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_BRVM";
#endif

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

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				if (!regMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(regMsg.Comment, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				var condition = regMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				await writer.WriteExpiryDateAsync(regMsg, DateParser, TimeZone, cancellationToken);

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);
				}

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				if (cancelMsg.OrderType != null)
				{
					await writer.WriteAsync(FixTags.OrdType, cancellationToken);
					await writer.WriteAsync(cancelMsg.GetFixType(), cancellationToken);
				}

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;
				var securityId = replaceMsg.SecurityId;

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(replaceMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				if (!replaceMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(replaceMsg.Comment, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixTimeInForce(), cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				if (!replaceMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(replaceMsg.ClientCode, cancellationToken);
				}

				var condition = replaceMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				if (replaceMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(replaceMsg.VisibleVolume.Value, cancellationToken);
				}

				await writer.WriteExpiryDateAsync(replaceMsg, DateParser, TimeZone, cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				if (statusMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
					await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
				}

				if (statusMsg.TransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrdStatusReqID, cancellationToken);
					await writer.WriteAsync(statusMsg.TransactionId, cancellationToken);
				}

				if (statusMsg.Side != null)
				{
					await writer.WriteSideAsync(statusMsg.Side.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, statusMsg.SecurityId, cancellationToken);

				return FixMessages.OrderStatusRequest;
			}

			case MessageTypes.OrderGroupCancel:
			{
				var cancelMsg = (OrderGroupCancelMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.MassCancelRequestType, cancellationToken);
				await writer.WriteAsync(MassCancelRequestType.CancelAllOrders, cancellationToken);

				return FixMessages.OrderMassCancelRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				return FixMessages.RequestForPositions;
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;
				var securityId = mdMsg.SecurityId;

				if (mdMsg.DataType2 == DataType.Level1 ||
					mdMsg.DataType2 == DataType.MarketDepth ||
					mdMsg.DataType2 == DataType.Ticks)
				{
				}
				else
				{
					return null;
				}

				await writer.WriteSubscriptionAsync(mdMsg, cancellationToken);

				if (mdMsg.MaxDepth != null)
				{
					await writer.WriteAsync(FixTags.MarketDepth, cancellationToken);
					await writer.WriteAsync(mdMsg.MaxDepth.Value, cancellationToken);
				}

				if (mdMsg.IsSubscribe)
				{
					await writer.WriteAsync(FixTags.MDUpdateType, cancellationToken);
					await writer.WriteAsync((int)MDUpdateType.IncrementalRefresh, cancellationToken);
				}

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				if (mdMsg.DataType2 == DataType.Level1)
				{
					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.Trade, cancellationToken);

					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.OpeningPrice, cancellationToken);

					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.TradingSessionHighPrice, cancellationToken);

					await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
					await writer.WriteAsync(MDEntryType.TradingSessionLowPrice, cancellationToken);

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

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				return FixMessages.MarketDataRequest;
			}

			case MessageTypes.SecurityLookup:
			{
				var lookupMsg = (SecurityLookupMessage)message;

				await writer.WriteAsync(FixTags.SecurityReqID, cancellationToken);
				await writer.WriteAsync(lookupMsg.TransactionId, cancellationToken);

				if (lookupMsg.SecurityId == default)
				{
					await writer.WriteAsync(FixTags.SecurityRequestType, cancellationToken);
					await writer.WriteAsync(4, cancellationToken); // All securities
				}
				else
				{
					await writer.WriteAsync(FixTags.SecurityRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityRequestType.RequestSecurityIdentityAndSpecifications, cancellationToken);

					await WriteSecurityIdAsync(writer, lookupMsg.SecurityId, cancellationToken);
				}

				await writer.WriteAsync(FixTags.SubscriptionRequestType, cancellationToken);
				await writer.WriteAsync(SubscriptionRequestType.Snapshot, cancellationToken);

				return FixMessages.SecurityDefinitionRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityId securityId, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

		if (!securityId.SecurityCode.IsEmpty())
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
	}
}