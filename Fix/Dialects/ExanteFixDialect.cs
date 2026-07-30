namespace StockSharp.Fix.Dialects;

/// <summary>
/// Exante FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.exante)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ExanteKey,
	GroupName = LocalizedStrings.RussiaKey)]
public class ExanteFixDialect : BaseFixDialect
{
	private static class ExanteFixMessages
	{
		public const string AccountSummaryRequest = "UASQ";
		public const string AccountSummaryResponse = "UASR";
		public const string AccountSummaryRequestReject = "UASJ";
	}

	private static class ExanteFixTags
	{
		public const FixTags AccSumReqID = (FixTags)20020;
		public const FixTags NumAccSumReports = (FixTags)20021;
		public const FixTags AccSumRejReason = (FixTags)20022;
		public const FixTags AccSumCurrency = (FixTags)20023;
		public const FixTags UsedMargin = (FixTags)20040;
		public const FixTags ProfitAndLoss = (FixTags)20030;
		public const FixTags ConvertedProfitAndLoss = (FixTags)20031;
		public const FixTags Value = (FixTags)20032;
		public const FixTags ConvertedValue = (FixTags)20033;
	}

	private const string _defaultSecurityIdSource = "111";
	private const string _defaultExchange = "EXANTE";

	/// <summary>
	/// Initializes a new instance of the <see cref="ExanteFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public ExanteFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new("hh:mm:ss.fff");
		DateParser = new("yyyyMMdd");

		//HasPosition = false;
	}

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => Messages.Extensions.AnyDepths;

	//public override bool IsNativeIdentifiers => true;

	//public override string StorageName => "Exante";

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => IsDemo && Address?.GetHost().StartsWithIgnoreCase("fixuat") == true ? base.FeatureName : "FIX_EXANTE";
#endif

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

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

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

				var condition = regMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);

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

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await WriteSecurityIdAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				var condition = replaceMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				if (statusMsg.OrderId != null || statusMsg.OriginalTransactionId != 0)
				{
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

					await WriteSecurityIdAsync(writer, statusMsg, cancellationToken);

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

				await WriteSecurityIdAsync(writer, mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.SecurityExchange, cancellationToken);
				await writer.WriteAsync(securityId.BoardCode, cancellationToken);

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

				if (!lookupMsg.CfiCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.SecurityListRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityListRequestType.SecurityTypeAndOrCficode, cancellationToken);

					await writer.WriteAsync(FixTags.CFICode, cancellationToken);
					await writer.WriteAsync(lookupMsg.CfiCode, cancellationToken);
				}
				else if (!lookupMsg.SecurityId.SecurityCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.SecurityListRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityListRequestType.Symbol, cancellationToken);

					await writer.WriteAsync(FixTags.Symbol, cancellationToken);
					await writer.WriteAsync(lookupMsg.SecurityId.SecurityCode, cancellationToken);
				}
				else
				{
					await writer.WriteAsync(FixTags.SecurityListRequestType, cancellationToken);
					await writer.WriteAsync((int)SecurityListRequestType.AllSecurities, cancellationToken);
				}

				return FixMessages.SecurityListRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var pfLookup = (PortfolioLookupMessage)message;

				await writer.WriteAsync(ExanteFixTags.AccSumReqID, cancellationToken);
				await writer.WriteAsync(pfLookup.TransactionId, cancellationToken);

				return ExanteFixMessages.AccountSummaryRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		var symbol = secMsg.Name;
		var securityId = secMsg.SecurityId.SecurityCode;

		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(symbol, cancellationToken);

		await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
		await writer.WriteAsync(securityId, cancellationToken);

		await writer.WriteAsync(FixTags.IDSource, cancellationToken);
		await writer.WriteAsync(_defaultSecurityIdSource, cancellationToken);
	}

	/// <inheritdoc />
	protected override IAsyncEnumerable<Message> ProcessExecutionReportAsync(ExecutionReport report, Func<ExecutionReport, ExecutionMessage, CancellationToken, IAsyncEnumerable<ExecutionMessage>> processExecMsg, CancellationToken cancellationToken)
	{
		if (report.Text == "No matching orders" || report.Symbol == "N/A")
			return default;

		return base.ProcessExecutionReportAsync(report, processExecMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<ExecutionMessage> ProcessExecutionReportAsync(ExecutionReport report, ExecutionMessage message, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		message.OrderStringId = null;

		var secId = message.SecurityId;

		secId.SecurityCode = report.SecurityId;

		if (secId.BoardCode.IsEmpty())
			secId.BoardCode = _defaultExchange;

		message.SecurityId = secId;

		await foreach (var r in base.ProcessExecutionReportAsync(report, message, cancellationToken))
			yield return r;
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case ExanteFixMessages.AccountSummaryResponse:
			{
				long? requestId = null;
				string account = null;
				var sendingTime = default(DateTime);
				string symbol = null;
				string id = null;
				string idSource = null;
				decimal? longQty = null;
				decimal? shortQty = null;
				decimal? avgPx = null;
				CurrencyTypes? currency = null;
				decimal? pnl = null;
				decimal? usedMargin = null;
				decimal? value = null;
				string cfiCode = null;
				string securityExchange = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case ExanteFixTags.AccSumReqID:
							requestId = await reader.ReadLongAsync(cancellationToken);
							return true;
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.SendingTime:
							sendingTime = await reader.ReadUtcAsync(TimeStampParser, cancellationToken);
							return true;
						case FixTags.Symbol:
							symbol = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.SecurityID:
							id = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.IDSource:
							idSource = await reader.ReadStringAsync(cancellationToken);
							return true;
						case ExanteFixTags.AccSumCurrency:
							currency = (await reader.ReadStringAsync(cancellationToken)).FromMicexCurrencyName(this.AddErrorLog);
							return true;
						case FixTags.LongQty:
							longQty = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.ShortQty:
							shortQty = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.AvgPx:
							avgPx = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case ExanteFixTags.ProfitAndLoss:
							pnl = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case ExanteFixTags.UsedMargin:
							usedMargin = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case ExanteFixTags.Value:
							value = await reader.ReadDecimalAsync(cancellationToken);
							return true;
						case FixTags.CFICode:
							cfiCode = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.SecurityExchange:
							securityExchange = await reader.ReadStringAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				var secId = new SecurityId
				{
					//Native = id,
					SecurityCode = id,
					BoardCode = securityExchange ?? _defaultExchange
				};

				if (secId.SecurityCode == id && id.TryParse(out CurrencyTypes _))
				{
					//secId.BoardCode = "EXANTE";
					yield return new SecurityMessage
					{
						SecurityId = secId,
						CfiCode = cfiCode,
					};
				}

				var msg = new PositionChangeMessage
				{
					PortfolioName = account,
					ServerTime = sendingTime,
					SecurityId = secId
				}
				.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl, true)
				.TryAdd(PositionChangeTypes.AveragePrice, avgPx, true)
				.TryAdd(PositionChangeTypes.BlockedValue, usedMargin, true)
				.TryAdd(PositionChangeTypes.CurrentValue, longQty > 0 ? longQty.Value : -shortQty.Value, true);

				if (currency != null)
					msg.Add(PositionChangeTypes.Currency, currency.Value);

				yield return msg;
				break;
			}
			case ExanteFixMessages.AccountSummaryRequestReject:
			{
				long? requestId = null;
				string account = null;
				string text = null;
				int? reason = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case ExanteFixTags.AccSumReqID:
							requestId = await reader.ReadLongAsync(cancellationToken);
							return true;
						case FixTags.Account:
							account = await reader.ReadStringAsync(cancellationToken);
							return true;
						case FixTags.Text:
							text = await reader.ReadStringAsync(cancellationToken);
							return true;
						case ExanteFixTags.AccSumRejReason:
							reason = await reader.ReadIntAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new InvalidOperationException($"RequestId={requestId}, account={account}, reason={reason}, text='{text}'.").ToErrorMessage();
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
	protected override void InitSecId(SecurityMessage message, string symbol, string securityExchange, string idSource, string idValue)
	{
		message.Name = symbol;

		message.SecurityId = new SecurityId
		{
			SecurityCode = idValue,
			BoardCode = securityExchange
		};
	}
}