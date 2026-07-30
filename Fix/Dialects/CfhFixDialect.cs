namespace StockSharp.Fix.Dialects;

/// <summary>
/// CFH FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CfhFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.cfh)]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CFHKey)]
public class CfhFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
	private static class CfhFixTags
	{
		public const FixTags QuoteRequestAction = (FixTags)5002;
		public const FixTags Balance = (FixTags)5020;
		public const FixTags AvailableForMarginTrading = (FixTags)5021;
		public const FixTags CreditLimit = (FixTags)5022;
		public const FixTags SecurityDeposit = (FixTags)5023;
		public const FixTags ClosedPnL = (FixTags)5024;
		public const FixTags OpenPnL = (FixTags)5025;
		public const FixTags MarginRequirement = (FixTags)5026;
		public const FixTags NetOpenPosition = (FixTags)5027;
	}

	private static class CfhFixMessages
	{
		public const string AccountInfoRequest = "AAA";
		public const string AccountInfo = "AAB";
	}

	private readonly FastDateTimeParser _expiryDateParser = new("yyyyMMdd");

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_CFH";
#endif

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

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				var condition = (FixOrderCondition)regMsg.Condition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				if (regMsg.Currency != null)
				{
					await writer.WriteAsync(FixTags.Currency, cancellationToken);
					await writer.WriteAsync(regMsg.Currency.Value.To<string>(), cancellationToken);
				}

				var tif = regMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (tif == FixTimeInForce.GoodTillDate)
				{
					await writer.WriteExpiryDateAsync(regMsg, _expiryDateParser, TimeZone, cancellationToken);
				}

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
					await writer.WriteAsync(1, cancellationToken);

					await writer.WriteAsync(FixTags.PartyID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);

					await writer.WriteAsync(FixTags.PartyIDSource, cancellationToken);
					await writer.WriteAsync(PartyIDSource.Mic, cancellationToken);

					await writer.WriteAsync(FixTags.PartyRole, cancellationToken);
					await writer.WriteAsync((int)PartyRole.ClientId, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;
				var securityId = cancelMsg.SecurityId;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (cancelMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);
				}

				if (cancelMsg.OrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(cancelMsg.OrderId.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

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

				if (replaceMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);
				}

				if (replaceMsg.OldOrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(replaceMsg.OldOrderId.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				var condition = (FixOrderCondition)replaceMsg.Condition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				var tif = replaceMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (tif == FixTimeInForce.GoodTillDate)
				{
					await writer.WriteExpiryDateAsync(replaceMsg, _expiryDateParser, TimeZone, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				if (statusMsg.OrderId != null || statusMsg.OriginalTransactionId != 0)
				{
					if (statusMsg.OrderId != null)
					{
						await writer.WriteAsync(FixTags.OrderID, cancellationToken);
						await writer.WriteAsync(statusMsg.OrderId.Value, cancellationToken);
					}

					if (statusMsg.OriginalTransactionId != 0)
					{
						await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
						await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
					}

					return FixMessages.OrderStatusRequest;
				}
				else
				{
					if (statusMsg.TransactionId != 0)
					{
						await writer.WriteAsync(FixTags.MassStatusReqID, cancellationToken);
						await writer.WriteAsync(statusMsg.TransactionId, cancellationToken);
					}

					await writer.WriteAsync(FixTags.MassStatusReqType, cancellationToken);
					await writer.WriteAsync((int)MassStatusReqType.StatusForAllOrders, cancellationToken);

					return FixMessages.OrderMassStatusRequest;
				}
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;

				if (!(mdMsg.DataType2 == DataType.Level1 || mdMsg.DataType2 == DataType.MarketDepth))
					return null;

				await writer.WriteAsync(FixTags.QuoteReqID, cancellationToken);
				await writer.WriteAsync(mdMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(CfhFixTags.QuoteRequestAction, cancellationToken);
				await writer.WriteAsync(mdMsg.IsSubscribe ? 0 : 1, cancellationToken);

				return FixMessages.QuoteRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var pfMsg = (PortfolioLookupMessage)message;

				if (!pfMsg.IsSubscribe)
					return null;

				await WriteAccountAsync(writer, pfMsg, cancellationToken);

				return CfhFixMessages.AccountInfoRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			// reading custom CFH message (not compatible with FIX standard)
			case CfhFixMessages.AccountInfo:
			{
				string account = null;
				var sendingTime = default(DateTime);
				decimal? closedPnL = null;
				decimal? openPnL = null;
				decimal? balance = null;
				CurrencyTypes? currency = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.SendingTime:
							sendingTime = await reader.ReadUtcAsync(TimeStampParser, cancellationToken);
							return true;
						case CfhFixTags.ClosedPnL:
							closedPnL = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case CfhFixTags.OpenPnL:
							openPnL = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case CfhFixTags.Balance:
							balance = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.Currency:
							currency = (await reader.ReadStringAsync(cancellationToken)).FromMicexCurrencyName(this.AddErrorLog);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				var msg = new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					PortfolioName = account,
					ServerTime = sendingTime
				}
				.TryAdd(PositionChangeTypes.RealizedPnL, closedPnL, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, openPnL, true)
				.TryAdd(PositionChangeTypes.CurrentValue, balance, true);

				if (currency != null)
					msg.Add(PositionChangeTypes.Currency, currency.Value);

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