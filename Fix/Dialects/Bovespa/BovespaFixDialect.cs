namespace StockSharp.Fix.Dialects.Bovespa;

/// <summary>
/// B3 BM&amp;F Bovespa FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.bovespa)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BrasilBolsaKey,
	GroupName = LocalizedStrings.StockKey)]
public class BovespaFixDialect : BaseFixDialect
{
	// ReSharper disable InconsistentNaming
	private static class BovespaFixTags
	{
		public const FixTags CancelOnDisconnectType = (FixTags)35002;
		public const FixTags CODTimeoutWindow = (FixTags)35003;
		public const FixTags PossMissingApplMsg = (FixTags)35033;
		public const FixTags MMProtectionReset = (FixTags)9773;
		public const FixTags Memo = (FixTags)5149;
		public const FixTags RoutingInstruction = (FixTags)35487;
		public const FixTags PrivateQuote = (FixTags)1171;
		public const FixTags UniqueTradeId = (FixTags)6032;

		public const FixTags MarketSegmentID = (FixTags)1300;
		public const FixTags MassActionReportID = (FixTags)1369;
		public const FixTags MassActionType = (FixTags)1373;
		public const FixTags MassActionScope = (FixTags)1374;
		public const FixTags MassActionResponse = (FixTags)1375;
		public const FixTags MassActionRejectReason = (FixTags)1376;
	}
	// ReSharper restore InconsistentNaming

	private static class BovespaFixMessages
	{
		public const string OrderMassActionRequest = "CA";
		public const string OrderMassActionReport = "BZ";
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BovespaFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BovespaFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh\\:mm\\:ss");

		OverrideExecIdByNative = true;
		ExchangeBoard = BoardCodes.Bovespa;
	}

	/// <inheritdoc />
	public override Type OrderConditionType => typeof(BovespaFixOrderCondition);

	/// <inheritdoc />
	protected override bool LoginAsPortfolioName => true;

	/// <inheritdoc />
	public override bool? IsPositionsEmulationRequired => true;

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages =>
	[
		//MessageTypes.MarketData.ToInfo(),
		//MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		//MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderGroupCancel.ToInfo(),
		//MessageTypes.OrderStatus.ToInfo(),

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
	public override string FeatureName => "FIX_BOVESPA";
#endif

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.Connect:
			{
				await writer.WriteAsync(FixTags.EncryptMethod, cancellationToken);
				await writer.WriteAsync((int)EncryptMethod.None, cancellationToken);

				await writer.WriteAsync(FixTags.HeartBtInt, cancellationToken);
				await writer.WriteAsync((int)HeartbeatInterval.TotalSeconds, cancellationToken);

				await writer.WriteAsync(FixTags.ResetSeqNumFlag, cancellationToken);
				await writer.WriteAsync(IsResetCounter, cancellationToken);

				if (!Password.IsEmpty())
				{
					var pwd = Password.UnSecure();
					var pwdData = pwd.UTF8();

					await writer.WriteAsync(FixTags.RawDataLength, cancellationToken);
					await writer.WriteAsync(pwdData.Length, cancellationToken);

					await writer.WriteAsync(FixTags.RawData, cancellationToken);
					await writer.WriteAsync(pwd, cancellationToken);
				}

				if (CancelOnDisconnect)
				{
					await writer.WriteAsync(BovespaFixTags.CancelOnDisconnectType, cancellationToken);
					await writer.WriteAsync('3', cancellationToken);
				}

				return FixMessages.Logon;
			}

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

				var condition = (BovespaFixOrderCondition)regMsg.Condition;

				if (condition?.MarketProtectionReset == true)
				{
					await writer.WriteAsync(BovespaFixTags.MMProtectionReset, cancellationToken);
					await writer.WriteAsync(true, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await WritePartiesAsync(writer, regMsg, cancellationToken);

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (regMsg.MinOrderVolume != null)
				{
					await writer.WriteAsync(FixTags.MinQty, cancellationToken);
					await writer.WriteAsync(regMsg.MinOrderVolume.Value, cancellationToken);
				}

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(GetOrdType(regMsg), cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				var tif = GetTimeInForce(regMsg);

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (tif == FixTimeInForce.GoodTillDate)
				{
					await writer.WriteExpiryDateAsync(regMsg, DateParser, TimeZone, cancellationToken);
				}

				await writer.WritePositionEffectAsync(regMsg.PositionEffect, cancellationToken);

				if (!regMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(BovespaFixTags.Memo, cancellationToken);
					await writer.WriteAsync(regMsg.Comment, cancellationToken);
				}

				if (condition?.IsRetailLiquidity != null)
				{
					await writer.WriteAsync(BovespaFixTags.RoutingInstruction, cancellationToken);
					await writer.WriteAsync(condition.IsRetailLiquidity.Value ? '1' : '2', cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				if (!cancelMsg.OrderStringId.IsEmpty())
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(cancelMsg.OrderStringId, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, cancelMsg, cancellationToken);

				await WritePartiesAsync(writer, cancelMsg, cancellationToken);

				await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				if (!cancelMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(BovespaFixTags.Memo, cancellationToken);
					await writer.WriteAsync(cancelMsg.Comment, cancellationToken);
				}

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				var condition = (BovespaFixOrderCondition)replaceMsg.Condition;

				if (condition?.MarketProtectionReset == true)
				{
					await writer.WriteAsync(BovespaFixTags.MMProtectionReset, cancellationToken);
					await writer.WriteAsync(true, cancellationToken);
				}

				if (!replaceMsg.OldOrderStringId.IsEmpty())
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(replaceMsg.OldOrderStringId, cancellationToken);
				}

				await WritePartiesAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				if (replaceMsg.MinOrderVolume != null)
				{
					await writer.WriteAsync(FixTags.MinQty, cancellationToken);
					await writer.WriteAsync(replaceMsg.MinOrderVolume.Value, cancellationToken);
				}

				if (replaceMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(replaceMsg.VisibleVolume.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(GetOrdType(replaceMsg), cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				var tif = GetTimeInForce(replaceMsg);

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (tif == FixTimeInForce.GoodTillDate)
				{
					await writer.WriteExpiryDateAsync(replaceMsg, DateParser, TimeZone, cancellationToken);
				}

				await writer.WritePositionEffectAsync(replaceMsg.PositionEffect, cancellationToken);

				if (!replaceMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(BovespaFixTags.Memo, cancellationToken);
					await writer.WriteAsync(replaceMsg.Comment, cancellationToken);
				}

				if (condition?.IsRetailLiquidity != null)
				{
					await writer.WriteAsync(BovespaFixTags.RoutingInstruction, cancellationToken);
					await writer.WriteAsync(condition.IsRetailLiquidity.Value ? '1' : '2', cancellationToken);
				}

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderGroupCancel:
			{
				var cancelMsg = (OrderGroupCancelMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(BovespaFixTags.MarketSegmentID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(BovespaFixTags.MassActionType, cancellationToken);
				await writer.WriteAsync(3, cancellationToken);

				await writer.WriteAsync(BovespaFixTags.MassActionScope, cancellationToken);
				await writer.WriteAsync(6, cancellationToken);

				await writer.WriteAsync(FixTags.ExecRestatementReason, cancellationToken);
				await writer.WriteAsync(202, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return BovespaFixMessages.OrderMassActionRequest;
			}

			//case MessageTypes.SecurityLookup:
			//{
			//	var lookupMsg = (SecurityLookupMessage)message;

			//	writer.Write(FixTags.SecurityReqID);
			//	writer.Write(lookupMsg.TransactionId);

			//	writer.Write(FixTags.SecurityRequestType);
			//	writer.Write((int)SecurityRequestType.RequestSecurityIdentityForTheSpecificationsProvided);

			//	writer.Write(FixTags.NoPartyIDs);
			//	writer.Write(0);

			//	writer.Write(FixTags.NoLegs);
			//	writer.Write(0);

			//	return FixMessages.SecurityDefinitionRequest;
			//}

			//case MessageTypes.MarketData:
			//{
			//	var mdMsg = (MarketDataMessage)message;

			//	if (mdMsg.DataType2 == DataType.Level1 ||
			//		mdMsg.DataType2 == DataType.MarketDepth)
			//	{
			//	}
			//	else
			//	{
			//		return null;
			//	}

			//	writer.Write(FixTags.QuoteReqID);
			//	writer.Write(mdMsg.GetRequestId());

			//	if (mdMsg.IsSubscribe)
			//	{
			//	}
			//	else
			//	{
			//		writer.Write(FixTags.QuoteCancelType);
			//		writer.Write((int)QuoteCancelType.CancelQuoteSpecifiedInQuoteid);
			//	}

			//	writer.Write(BovespaFixTags.PrivateQuote);
			//	writer.Write(false);

			//	writer.Write(mdMsg.IsSubscribe ? FixTags.NoRelatedSym : FixTags.NoQuoteEntries);
			//	writer.Write(1);

			//	WriteSecurityId(writer, mdMsg);

			//	return mdMsg.IsSubscribe ? FixMessages.QuoteRequest : FixMessages.QuoteCancel;
			//}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private const char _atClose = '7';
	private const char _goodForAuction = 'A';

	private static char GetTimeInForce(OrderRegisterMessage regMsg)
	{
		var condition = (BovespaFixOrderCondition)regMsg.Condition;

		if (condition?.TimeInForce != null)
		{
			return condition.TimeInForce switch
			{
				BovespaFixTimeInForce.AtClose => _atClose,
				BovespaFixTimeInForce.GoodForAuction => _goodForAuction,
				_ => throw new ArgumentOutOfRangeException(condition.TimeInForce.ToString()),
			};
		}

		return regMsg.GetFixTimeInForce();
	}

	/// <inheritdoc />
	protected override async ValueTask<bool> ProcessExecutionReportExtraTagAsync(FixTags tag, IFixReader reader, ExecutionReport report, CancellationToken cancellationToken)
	{
		switch (tag)
		{
			case BovespaFixTags.Memo:
				report.Text = await reader.ReadStringAsync(cancellationToken);
				return true;

			case BovespaFixTags.MMProtectionReset:
				report.ExtraFields[tag] = await reader.ReadBoolAsync(cancellationToken);
				return true;

			case BovespaFixTags.RoutingInstruction:
				report.ExtraFields[tag] = await reader.ReadCharAsync(cancellationToken);
				return true;

			case BovespaFixTags.UniqueTradeId:
				if (OverrideExecIdByNative)
				{
					report.ExtraFields[tag] = await reader.ReadLongAsync(cancellationToken);
					return true;
				}

				break;
		}

		return await base.ProcessExecutionReportExtraTagAsync(tag, reader, report, cancellationToken);
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<ExecutionMessage> ProcessExecutionReportAsync(ExecutionReport report, ExecutionMessage message, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var condition = (BovespaFixOrderCondition)message.Condition;

		if (condition is null)
			message.Condition = condition = new BovespaFixOrderCondition();

		switch (report.TimeInForce)
		{
			case _atClose:
				condition.TimeInForce = BovespaFixTimeInForce.AtClose;
				break;
			case _goodForAuction:
				condition.TimeInForce = BovespaFixTimeInForce.GoodForAuction;
				break;
		}

		if (report.ExtraFields.TryGetValue(BovespaFixTags.MMProtectionReset, out var mm) && (bool)mm)
		{
			condition.MarketProtectionReset = true;
		}

		if (report.ExtraFields.TryGetValue(BovespaFixTags.RoutingInstruction, out var instruction))
		{
			condition.IsRetailLiquidity = (char)instruction == '1';
		}

		if (OverrideExecIdByNative && report.ExtraFields.TryGetValue(BovespaFixTags.UniqueTradeId, out var tradeId))
		{
			message.TradeId = (long)tradeId;
			message.TradeStringId = null;
		}

		await foreach (var r in base.ProcessExecutionReportAsync(report, message, cancellationToken))
			yield return r;
	}

	/// <inheritdoc />
	protected override void ProcessParties(ExecutionReport report)
	{
		report.ClientId = report.Parties.Select(p => $"{p.PartyId}={(int)p.PartyRole}").JoinDotComma();
	}

	/// <inheritdoc />
	protected override string GetBoardCode(string destination, string exchange, string tradingSession)
	{
		if (ExchangeBoard.IsEmpty())
			return base.GetBoardCode(destination, exchange, tradingSession);

		return ExchangeBoard;
	}

	private const char _marketLeftOrder = 'K';
	private const char _retailLiquidityProvider = 'W';

	/// <inheritdoc />
	protected override OrderTypes GetOrderType(ExecutionReport report, out OrderCondition condition)
	{
		switch (report.OrdType)
		{
			case _marketLeftOrder:
				condition = new BovespaFixOrderCondition { TypeEx = BovespaFixOrderTypes.MarketLeftOverLimit };
				return OrderTypes.Conditional;

			case _retailLiquidityProvider:
				condition = new BovespaFixOrderCondition { TypeEx = BovespaFixOrderTypes.RetailLiquidityProvider };
				return OrderTypes.Conditional;

			default:
				var type = base.GetOrderType(report, out condition);

				if (condition is FixOrderCondition)
					condition = new BovespaFixOrderCondition();

				return type;
		}
	}

	private static char GetOrdType(OrderRegisterMessage regMsg)
	{
		var condition = (BovespaFixOrderCondition)regMsg.Condition;

		if (condition?.TypeEx != null)
		{
			switch (condition.TypeEx)
			{
				case BovespaFixOrderTypes.MarketLeftOverLimit:
					return _marketLeftOrder;
				case BovespaFixOrderTypes.RetailLiquidityProvider:
					return _retailLiquidityProvider;
				default:
					throw new ArgumentOutOfRangeException(condition.TypeEx.ToString());
			}
		}

		return regMsg.GetFixType();
	}

	private static async ValueTask WritePartiesAsync(IFixWriter writer, OrderMessage message, CancellationToken cancellationToken)
	{
		var parties = message.ClientCode.SplitByDotComma(true);

		await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
		await writer.WriteAsync(parties.Length, cancellationToken);

		foreach (var party in parties)
		{
			var parts = party.SplitByEqual(false);

			await writer.WriteAsync(FixTags.PartyID, cancellationToken);
			await writer.WriteAsync(parts[0], cancellationToken);

			await writer.WriteAsync(FixTags.PartyIDSource, cancellationToken);
			await writer.WriteAsync(PartyIDSource.Proprietary, cancellationToken);

			if (parts.Length > 1)
			{
				await writer.WriteAsync(FixTags.PartyRole, cancellationToken);
				await writer.WriteAsync(parts[1], cancellationToken);
			}
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, ISecurityIdMessage message, CancellationToken cancellationToken)
	{
		var securityId = message.SecurityId;

		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case BovespaFixMessages.OrderMassActionReport:
			{
				long? clOrdId = null;
				string reportId = null;
				int? response = null;
				int? rejectReason = null;
				int? reason = null;
				string text = null;
				DateTime? transactTime = null;
				DateTime? sendingTime = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case FixTags.ClOrdID:
							clOrdId = await reader.ReadLongAsync(cancellationToken);
							return true;
						case BovespaFixTags.MassActionReportID:
							reportId = await reader.ReadStringAsync(cancellationToken);
							return true;
						case BovespaFixTags.MassActionType:
							await reader.ReadIntAsync(cancellationToken);
							return true;
						case BovespaFixTags.MassActionScope:
							await reader.ReadIntAsync(cancellationToken);
							return true;
						case BovespaFixTags.MassActionResponse:
							response = await reader.ReadIntAsync(cancellationToken);
							return true;
						case BovespaFixTags.MassActionRejectReason:
							rejectReason = await reader.ReadIntAsync(cancellationToken);
							return true;
						case FixTags.ExecRestatementReason:
							reason = await reader.ReadIntAsync(cancellationToken);
							return true;
						case FixTags.TransactTime:
							transactTime = await reader.ReadDateTimeAsync(TimeStampParser, cancellationToken);
							return true;
						case FixTags.SendingTime:
							sendingTime = await reader.ReadDateTimeAsync(TimeStampParser, cancellationToken);
							return true;
						case FixTags.Text:
							text = await reader.ReadStringAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = clOrdId ?? default,
					Error = rejectReason != 0 ? new InvalidOperationException($"Code {rejectReason}. Text '{text}'.") : null,
					ServerTime = transactTime ?? sendingTime ?? DateTime.UtcNow,
				};
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

	/// <inheritdoc />
	protected override async ValueTask<bool> ProcessSequenceResetExtraTagAsync(FixTags tag, IFixReader reader, FixSeqResetMessage message, CancellationToken cancellationToken)
	{
		switch (tag)
		{
			case BovespaFixTags.PossMissingApplMsg:
				message.PossMissingApplMsg = await reader.ReadBoolAsync(cancellationToken);
				return true;
			default:
				return false;
		}
	}
}