namespace StockSharp.Fix.Dialects;

/// <summary>
/// QUIK server FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.quik)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuikServerKey,
	GroupName = LocalizedStrings.RussiaKey)]
public class QuikServerFixDialect : BaseFixDialect
{
	private static class QuikFixTags
	{
		public const FixTags BaseAsset = (FixTags)5041;

		public const FixTags PosUpdateAction = (FixTags)5024;
		public const FixTags NoPosKeys = (FixTags)5025;
		public const FixTags PosKeyType = (FixTags)5026;
		public const FixTags PosKeyValue = (FixTags)5027;
		public const FixTags NoPosItems = (FixTags)5028;
		public const FixTags PosItemType = (FixTags)5029;
		public const FixTags PosItemValue = (FixTags)5030;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="QuikServerFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public QuikServerFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix42)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		TickAsLevel1 = true;
		//OrderLookup = true;
		//HasPosition = false;
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_QUIKSERVER";
#endif

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => Messages.Extensions.AnyDepths;

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

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

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await WriteCurrencyAsync(writer, regMsg, cancellationToken);

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

				if (regMsg.TillDate?.IsToday() == false)
				{
					await writer.WriteAsync(FixTags.ExpireTime, cancellationToken);
					await writer.WriteAsync(regMsg.TillDate.Value, TimeStampParser, cancellationToken);
				}

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);
				}

				if (regMsg.IsMarketMaker == true)
				{
					await writer.WriteAsync(FixTags.OrderRestrictions, cancellationToken);
					await writer.WriteAsync(5, cancellationToken);
				}

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				if (regMsg.OrderType == OrderTypes.Conditional && condition?.Type == FixStopOrderTypes.TakeProfit && condition.Offset != null)
				{
					await writer.WriteAsync(FixTags.PegOffsetValue, cancellationToken);
					await writer.WriteAsync(condition.Offset.Value, cancellationToken);

					await writer.WriteAsync(FixTags.PegOffsetType, cancellationToken);
					await writer.WriteAsync(0, cancellationToken);
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

				//if (cancelMsg.OrderType != null)
				//{
				//	await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				//	await writer.WriteAsync(cancelMsg.GetFixType(), cancellationToken);
				//}

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				//if (cancelMsg.OrderId != null)
				//{
				//	await writer.WriteAsync(FixTags.OrderID, cancellationToken);
				//	await writer.WriteAsync(cancelMsg.OrderId.Value, cancellationToken);
				//}

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				//if (statusMsg.OrderId != null)
				//{
				//	await writer.WriteAsync(FixTags.OrderID, cancellationToken);
				//	await writer.WriteAsync(statusMsg.OrderId.Value, cancellationToken);
				//}

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

				if (statusMsg.SecurityId != default)
					await WriteSecurityIdAsync(writer, statusMsg, cancellationToken);

				return FixMessages.OrderStatusRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var lookupMgs = (PortfolioLookupMessage)message;

				await writer.WriteAsync(FixTags.PosReqID, cancellationToken);
				await writer.WriteAsync(lookupMgs.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.SubscriptionRequestType, cancellationToken);
				await writer.WriteAsync(SubscriptionRequestType.SnapshotPlusUpdates, cancellationToken);

				return FixMessages.RequestForPositions;
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;

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

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, mdMsg, cancellationToken);

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

				if (lookupMsg.SecurityId == default)
				{
					await writer.WriteAsync(FixTags.SecurityRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityRequestType.RequestListSecurities, cancellationToken);
				}
				else
				{
					await writer.WriteAsync(FixTags.SecurityRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityRequestType.RequestSecurityIdentityAndSpecifications, cancellationToken);

					await WriteSecurityIdAsync(writer, lookupMsg, cancellationToken);
				}

				return FixMessages.SecurityDefinitionRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	/// <summary>
	/// Read order condition.
	/// </summary>
	/// <param name="reader">The reader of data recorded in the FIX protocol format.</param>
	/// <param name="tag">Tag.</param>
	/// <param name="getCondition">Condition.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Result.</returns>
	protected virtual async ValueTask<bool> ReadOrderConditionAsync(IFixReader reader, FixTags tag, Func<OrderCondition> getCondition, CancellationToken cancellationToken)
	{
		switch (tag)
		{
			case FixTags.StopPx:
				((FixOrderCondition)getCondition()).StopLoss = await reader.ReadDecimalAsync(cancellationToken);
				return true;

			case FixTags.PegOffsetValue:
				((FixOrderCondition)getCondition()).Offset = await reader.ReadDecimalAsync(cancellationToken);
				return true;

			default:
				return false;
		}
	}

	private static async ValueTask WriteCurrencyAsync(IFixWriter writer, SecurityMessage message, CancellationToken cancellationToken)
	{
		if (message.Currency == null)
			return;

		await writer.WriteAsync(FixTags.Currency, cancellationToken);
		await writer.WriteAsync(message.Currency.Value.ToMicexCurrencyName(), cancellationToken);
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.Name, cancellationToken);

		var securityId = secMsg.SecurityId;

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

		await writer.WriteAsync(FixTags.ExDestination, cancellationToken);
		await writer.WriteAsync(securityId.BoardCode, cancellationToken);
	}

	/// <inheritdoc />
	protected override IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, CancellationToken cancellationToken)
	{
		return msgType switch
		{
			FixMessages.PositionReport => ReadPositionReportAsync(reader, cancellationToken),
			_ => base.OnReadAsync(reader, msgType, cancellationToken),
		};
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.ToLowerInvariant().EqualsIgnoreCase("Reply to Logout");
	}

	/// <inheritdoc />
	protected async override ValueTask<bool> ProcessSecurityDefinitionAsync(FixTags tag, IFixReader reader, SecurityMessage message, CancellationToken cancellationToken)
	{
		var secId = message.SecurityId;

		switch (tag)
		{
			case FixTags.Symbol:
				message.Name = await reader.ReadStringAsync(cancellationToken);
				return true;

			case FixTags.UnderlyingSecurityID:
				secId.SecurityCode = await reader.ReadStringAsync(cancellationToken);
				message.SecurityId = secId;
				return true;

			case FixTags.UnderlyingSymbol:
				message.ShortName = await reader.ReadStringAsync(cancellationToken);
				return true;

			case FixTags.UnderlyingSecurityDesc:
				secId.Isin = await reader.ReadStringAsync(cancellationToken);
				message.SecurityId = secId;
				return true;

			case FixTags.UnderlyingSymbolSfx:
				var parts = (await reader.ReadStringAsync(cancellationToken)).Split('-');
				message.Decimals = parts[0].To<int>();
				message.PriceStep = parts[1].To<int>();
				return true;

			case FixTags.UnderlyingPutOrCall:
				message.OptionType = await reader.ReadStringAsync(cancellationToken) == "1" ? OptionTypes.Call : OptionTypes.Put;
				return true;

			case FixTags.UnderlyingStrikePrice:
				message.Strike = await reader.ReadDecimalAsync(cancellationToken);
				return true;

			case FixTags.UnderlyingSecurityType:
				switch (await reader.ReadStringAsync(cancellationToken))
				{
					case "OPT":
						message.SecurityType = SecurityTypes.Option;
						break;

					case "FUT":
						message.SecurityType = SecurityTypes.Future;
						break;
				}

				return true;

			case QuikFixTags.BaseAsset:
				message.TryFillUnderlyingId(await reader.ReadStringAsync(cancellationToken));
				return true;
		}

		return await base.ProcessSecurityDefinitionAsync(tag, reader, message, cancellationToken);
	}

	private class Party
	{
		public string PartyId { get; set; }
		public string PartyIdSource { get; set; }
		public int PartyRole { get; set; }
	}

	private class Position
	{
		public string PosType { get; set; }
		public int PosUpdateAction { get; set; }
		public PosElem[] PosKeys { get; set; }
		public PosElem[] PosItems { get; set; }
	}

	private class PosElem
	{
		public string Type { get; set; }
		public string Value { get; set; }
	}

	private async IAsyncEnumerable<Message> ReadPositionReportAsync(IFixReader reader, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var sendingTime = default(DateTime);
		string posMaintRptID = null;
		int? posReqType = null;
		string unsolicitedIndicator = null;
		string clearingBusinessDate = null;
		Party[] parties = null;
		var partyIndex = 0;
		Position[] positions = null;
		var posIndex = 0;
		var posKeyIndex = 0;
		var posItemIndex = 0;

		var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
		{
			switch (tag)
			{
				case FixTags.SendingTime:
					sendingTime = await reader.ReadUtcAsync(TimeStampParser, cancellationToken);
					return true;

				case FixTags.PosMaintRptID:
					posMaintRptID = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.PosReqType:
					posReqType = await reader.ReadIntAsync(cancellationToken);
					return true;
				case FixTags.UnsolicitedIndicator:
					unsolicitedIndicator = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.ClearingBusinessDate:
					clearingBusinessDate = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.NoPartyIDs:
					parties = new Party[await reader.ReadIntAsync(cancellationToken)];

					for (var i = 0; i < parties.Length; i++)
						parties[i] = new Party();

					return true;
				case FixTags.PartyID:
					if (parties[partyIndex].PartyId != null)
						partyIndex++;

					parties[partyIndex].PartyId = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.PartyIDSource:
					if (parties[partyIndex].PartyIdSource != null)
						partyIndex++;

					parties[partyIndex].PartyIdSource = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.PartyRole:
					if (parties[partyIndex].PartyRole != 0)
						partyIndex++;

					parties[partyIndex].PartyRole = await reader.ReadIntAsync(cancellationToken);
					return true;

				case FixTags.NoPositions:
					positions = new Position[await reader.ReadIntAsync(cancellationToken)];

					for (var i = 0; i < positions.Length; i++)
						positions[i] = new Position();

					return true;
				case FixTags.PosType:
					if (positions[posIndex].PosType != null)
					{
						posIndex++;
						posKeyIndex = 0;
						posItemIndex = 0;
					}

					positions[posIndex].PosType = await reader.ReadStringAsync(cancellationToken);
					return true;
				case QuikFixTags.PosUpdateAction:
					if (positions[posIndex].PosUpdateAction != 0)
					{
						posIndex++;
						posKeyIndex = 0;
						posItemIndex = 0;
					}

					positions[posIndex].PosUpdateAction = await reader.ReadIntAsync(cancellationToken);
					return true;
				case QuikFixTags.NoPosKeys:
					if (positions[posIndex].PosKeys != null)
					{
						posIndex++;
						posKeyIndex = 0;
						posItemIndex = 0;
					}

					positions[posIndex].PosKeys = new PosElem[await reader.ReadIntAsync(cancellationToken)];

					for (var i = 0; i < positions[posIndex].PosKeys.Length; i++)
						positions[posIndex].PosKeys[i] = new PosElem();

					return true;
				case QuikFixTags.PosKeyType:
					if (positions[posIndex].PosKeys[posKeyIndex].Type != null)
						posKeyIndex++;

					positions[posIndex].PosKeys[posKeyIndex].Type = await reader.ReadStringAsync(cancellationToken);
					return true;
				case QuikFixTags.PosKeyValue:
					if (positions[posIndex].PosKeys[posKeyIndex].Value != null)
						posKeyIndex++;

					positions[posIndex].PosKeys[posKeyIndex].Value = await reader.ReadStringAsync(cancellationToken);
					return true;

				case QuikFixTags.NoPosItems:
					if (positions[posIndex].PosItems != null)
					{
						posIndex++;
						posKeyIndex = 0;
						posItemIndex = 0;
					}

					positions[posIndex].PosItems = new PosElem[await reader.ReadIntAsync(cancellationToken)];

					for (var i = 0; i < positions[posIndex].PosItems.Length; i++)
						positions[posIndex].PosItems[i] = new PosElem();

					return true;
				case QuikFixTags.PosItemType:
					if (positions[posIndex].PosItems[posItemIndex].Type != null)
						posItemIndex++;

					positions[posIndex].PosItems[posItemIndex].Type = await reader.ReadStringAsync(cancellationToken);
					return true;
				case QuikFixTags.PosItemValue:
					if (positions[posIndex].PosItems[posItemIndex].Value != null)
						posItemIndex++;

					positions[posIndex].PosItems[posItemIndex].Value = await reader.ReadStringAsync(cancellationToken);
					return true;

				default:
					return false;
			}
		}, cancellationToken);

		if (!isOk)
			yield break;

		if (parties == null)
			yield break;

		var clientCode = parties.FirstOrDefault(p => p.PartyRole == 3)?.PartyId;
		var account = parties.FirstOrDefault(p => p.PartyRole == 38)?.PartyId ?? parties.FirstOrDefault(p => p.PartyRole == 13)?.PartyId;

		if (!account.IsEmpty())
		{
			yield return new PortfolioMessage
			{
				PortfolioName = account,
				ClientCode = clientCode,
			};
		}

		if (positions == null)
			yield break;

		foreach (var position in positions)
		{
			var sec = position.PosKeys.FirstOrDefault(k => k.Type == "SEC")?.Value;
			var acc = position.PosKeys.FirstOrDefault(k => k.Type == "ACC")?.Value;

			if (sec.IsEmpty())
				continue;

			var posMsg = new PositionChangeMessage
			{
				ServerTime = sendingTime,
				PortfolioName = acc ?? account,
				SecurityId = new SecurityId
				{
					SecurityCode = sec,
					BoardCode = "MICEX"
				},
			};

			foreach (var item in position.PosItems)
			{
				switch (item.Type)
				{
					case "OPEN_BAL":
						posMsg.TryAdd(PositionChangeTypes.BeginValue, item.Value.To<decimal?>(), true);
						break;
					case "CUR_BAL":
						posMsg.TryAdd(PositionChangeTypes.CurrentValue, item.Value.To<decimal?>(), true);
						break;
					case "LOCKED":
						posMsg.TryAdd(PositionChangeTypes.BlockedValue, item.Value.To<decimal?>(), true);
						break;
					case "AVG_POS_PRICE":
						posMsg.TryAdd(PositionChangeTypes.AveragePrice, item.Value.To<decimal?>(), true);
						break;
					case "TS_COMM":
						posMsg.TryAdd(PositionChangeTypes.Commission, item.Value.To<decimal?>(), true);
						break;
				}
			}

			yield return posMsg;
		}
	}

	/// <inheritdoc />
	protected override SecurityStates? FromSecurityTradingStatus(int? status)
	{
		switch (status)
		{
			case null:
				return null;
			case 17:
				return SecurityStates.Trading;
			default:
				return SecurityStates.Stoped;
		}
	}
}