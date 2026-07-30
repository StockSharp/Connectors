namespace StockSharp.Fix.Dialects;

/// <summary>
/// SwissQuote FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.swissquote)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SwissQuoteKey,
	GroupName = LocalizedStrings.ForexKey)]
public class SwissQuoteFixDialect : BaseFixDialect
{
	private const MessageTypes _swissMassQuoteCancel = (MessageTypes)(-5100);

	private class SwissMassQuoteCancelMessage : Message
	{
		public SwissMassQuoteCancelMessage()
			: base(_swissMassQuoteCancel)
		{
		}

		public string QuoteReqId { get; set; }
		public string QuoteId { get; set; }

		public override Message Clone()
		{
			return new SwissMassQuoteCancelMessage
			{
				QuoteReqId = QuoteReqId,
				QuoteId = QuoteId,
			};
		}
	}

	private static class SwissQuoteFixTags
	{
		public const FixTags ClientSlippage = (FixTags)10106;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SwissQuoteFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public SwissQuoteFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss.fff");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		//HasPosition = false;

		ExchangeBoard = BoardCodes.SwSq;
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_SWISSQUOTE";
#endif

	private const int _maxDepth = 3;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [.. Enumerable.Range(1, _maxDepth)];

	/// <inheritdoc />
	protected override bool LoginAsPortfolioName => true;

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

		_swissMassQuoteCancel.ToInfo(true)
	];

	/// <inheritdoc />
	public override async IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
	{
		yield return DataType.Level1;
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		const string securities = @"XAG/AUD	SWSQ	Currency
XAG/CHF	SWSQ	Currency
XAG/EUR	SWSQ	Currency
XAG/GBP	SWSQ	Currency
XAG/USD	SWSQ	Currency
USD/ILS	SWSQ	Currency
EUR/ILS	SWSQ	Currency
USD/HUF	SWSQ	Currency
AUD/CAD	SWSQ	Currency
AUD/CHF	SWSQ	Currency
AUD/NZD	SWSQ	Currency
CAD/CHF	SWSQ	Currency
CHF/JPY	SWSQ	Currency
GBP/AUD	SWSQ	Currency
GBP/NZD	SWSQ	Currency
USD/CZK	SWSQ	Currency
EUR/HUF	SWSQ	Currency
EUR/CZK	SWSQ	Currency
GBP/PLN	SWSQ	Currency
EUR/NZD	SWSQ	Currency
NZD/CAD	SWSQ	Currency
NZD/CHF	SWSQ	Currency
USD/RUB	SWSQ	Currency
SEK/JPY	SWSQ	Currency
NOK/JPY	SWSQ	Currency
NZD/SEK	SWSQ	Currency
OIL/USD	SWSQ	Currency
DAX/EUR	SWSQ	Currency
ESX/EUR	SWSQ	Currency
USD/BRL	SWSQ	Currency
USD/CNY	SWSQ	Currency
USD/INR	SWSQ	Currency
ALL/USD	SWSQ	Currency
NGC/USD	SWSQ	Currency
NIL/USD	SWSQ	Currency
CUL/USD	SWSQ	Currency
CAD/SGD	SWSQ	Currency
CHF/HUF	SWSQ	Currency
CHF/PLN	SWSQ	Currency
CHF/SGD	SWSQ	Currency
EUR/HKD	SWSQ	Currency
EUR/SGD	SWSQ	Currency
EUR/ZAR	SWSQ	Currency
GBP/HUF	SWSQ	Currency
GBP/SGD	SWSQ	Currency
GBP/TRY	SWSQ	Currency
HKD/JPY	SWSQ	Currency
NZD/DKK	SWSQ	Currency
NZD/SGD	SWSQ	Currency
SGD/HKD	SWSQ	Currency
SGD/JPY	SWSQ	Currency
TRY/JPY	SWSQ	Currency
USD/CNH	SWSQ	Currency
EUR/RUB	SWSQ	Currency
ZAR/JPY	SWSQ	Currency
MXN/JPY	SWSQ	Currency
CNH/JPY	SWSQ	Currency
PLN/JPY	SWSQ	Currency
EUR/USD	SWSQ	Currency
EUR/CHF	SWSQ	Currency
USD/CHF	SWSQ	Currency
USD/JPY	SWSQ	Currency
GBP/USD	SWSQ	Currency
EUR/GBP	SWSQ	Currency
AUD/USD	SWSQ	Currency
EUR/JPY	SWSQ	Currency
USD/CAD	SWSQ	Currency
GBP/JPY	SWSQ	Currency
GBP/CHF	SWSQ	Currency
USD/HKD	SWSQ	Currency
USD/SGD	SWSQ	Currency
AUD/JPY	SWSQ	Currency
NZD/USD	SWSQ	Currency
USD/SEK	SWSQ	Currency
USD/ZAR	SWSQ	Currency
GBP/NOK	SWSQ	Currency
EUR/NOK	SWSQ	Currency
CHF/NOK	SWSQ	Currency
USD/MXN	SWSQ	Currency
EUR/PLN	SWSQ	Currency
USD/PLN	SWSQ	Currency
GBP/CAD	SWSQ	Currency
USD/TRY	SWSQ	Currency
EUR/TRY	SWSQ	Currency
EUR/CAD	SWSQ	Currency
USD/DKK	SWSQ	Currency
EUR/DKK	SWSQ	Currency
EUR/SEK	SWSQ	Currency
GBP/SEK	SWSQ	Currency
GBP/DKK	SWSQ	Currency
CHF/DKK	SWSQ	Currency
CHF/SEK	SWSQ	Currency
DKK/SEK	SWSQ	Currency
NOK/SEK	SWSQ	Currency
CAD/JPY	SWSQ	Currency
NZD/JPY	SWSQ	Currency
EUR/AUD	SWSQ	Currency
SMI/CHF	SWSQ	Currency
#EU50	SWSQ	Commodity
#FR40	SWSQ	Commodity
#DE30	SWSQ	Commodity
#CH	SWSQ	Commodity
#GB100	SWSQ	Commodity
#DJ30	SWSQ	Commodity
#SP500	SWSQ	Commodity
#NAS100	SWSQ	Commodity
#ES	SWSQ	Commodity
#AU200U	SWSQ	Commodity
#SP500U	SWSQ	Commodity
#DJ30U	SWSQ	Commodity
XPD/USD	SWSQ	Currency
XPT/USD	SWSQ	Currency
LCO/USD	SWSQ	Currency
XAU/AUD	SWSQ	Currency
XAU/CAD	SWSQ	Currency
XAU/CHF	SWSQ	Currency
XAU/EUR	SWSQ	Currency
XAU/GBP	SWSQ	Currency
#USOILQ	SWSQ	Commodity
#UKOILU	SWSQ	Commodity
CUC/USD	SWSQ	Currency
XAG/CAD	SWSQ	Currency
#USBNDU	SWSQ	Commodity
#BUNDU	SWSQ	Commodity
#NGQ	SWSQ	Commodity
#LGILTU	SWSQ	Commodity
XAU/USD	SWSQ	Currency
#HGCU	SWSQ	Commodity
USD/NOK	SWSQ	Currency
#RUSU	SWSQ	Commodity
#HSIN	SWSQ	Commodity
PBL/USD	SWSQ	Currency
ZNL/USD	SWSQ	Currency
#DE30U	SWSQ	Commodity
#HGQ	SWSQ	Commodity
#GASQ	SWSQ	Commodity
#NIK225U	SWSQ	Commodity
#GSOQ	SWSQ	Commodity
#GB100U	SWSQ	Commodity
#NLN	SWSQ	Commodity
#CHU	SWSQ	Commodity
#NAS100U	SWSQ	Commodity
#ESN	SWSQ	Commodity
#FR40N	SWSQ	Commodity
#EU50U	SWSQ	Commodity";

		foreach (var line in securities.SplitByRN())
		{
			var cells = line.SplitByTab();

			await RaiseNewOutMessageAsync(new SecurityMessage
			{
				SecurityId = new SecurityId
				{
					SecurityCode = cells[0],
					BoardCode = cells[1],
				},
				SecurityType = cells[2].To<SecurityTypes>(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		await RaiseNewOutMessageAsync(lookupMsg.CreateResult(), cancellationToken);
	}

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

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(regMsg.SecurityId.SecurityCode, cancellationToken);

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

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				if (regMsg.Slippage != null)
				{
					await writer.WriteAsync(SwissQuoteFixTags.ClientSlippage, cancellationToken);
					await writer.WriteAsync(regMsg.Slippage.Value, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.OrderId ?? 0, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(cancelMsg.SecurityId.SecurityCode, cancellationToken);

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

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(replaceMsg.SecurityId.SecurityCode, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixTimeInForce(), cancellationToken);

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

					await writer.WriteAsync(FixTags.Symbol, cancellationToken);
					await writer.WriteAsync(statusMsg.SecurityId.SecurityCode, cancellationToken);

					if (statusMsg.Side != null)
					{
						await writer.WriteSideAsync(statusMsg.Side.Value, cancellationToken);
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

				if (mdMsg.DataType2 == DataType.Level1)
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

				await writer.WriteAsync(FixTags.Symbol, cancellationToken);
				await writer.WriteAsync(mdMsg.SecurityId.SecurityCode, cancellationToken);

				//await writer.WriteAsync(FixTags.SecurityDesc, cancellationToken);
				//await writer.WriteAsync(mdMsg.SecurityId.BoardCode, cancellationToken);

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Bid, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Offer, cancellationToken);

				return FixMessages.MarketDataRequest;
			}

			case _swissMassQuoteCancel:
			{
				var cancelMsg = (SwissMassQuoteCancelMessage)message;

				await writer.WriteAsync(FixTags.QuoteReqID, cancellationToken);
				await writer.WriteAsync(cancelMsg.QuoteReqId, cancellationToken);

				await writer.WriteAsync(FixTags.QuoteCancelType, cancellationToken);
				await writer.WriteAsync((int)QuoteCancelType.CancelAllQuotes, cancellationToken);

				await writer.WriteAsync(FixTags.QuoteID, cancellationToken);
				await writer.WriteAsync(cancelMsg.QuoteId, cancellationToken);

				return FixMessages.QuoteCancel;
			}

			case MessageTypes.PortfolioLookup:
			{
				var lookupMsg = (PortfolioLookupMessage)message;

				await writer.WriteAsync(FixTags.PosReqID, cancellationToken);
				await writer.WriteAsync(lookupMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.PosReqType, cancellationToken);
				await writer.WriteAsync((int)PosReqType.Positions, cancellationToken);

				await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
				await writer.WriteAsync(0, cancellationToken);

				await writer.WriteAsync(FixTags.Account, cancellationToken);
				await writer.WriteAsync(0, cancellationToken);

				await writer.WriteAsync(FixTags.AccountType, cancellationToken);
				await writer.WriteAsync((int)AccountType.AccountIsCarriedOnCustomerSideBooks, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.ClearingBusinessDate, cancellationToken);
				await writer.WriteAsync(DateTime.UtcNow, DateParser, cancellationToken);

				await writer.WriteAsync(FixTags.SubscriptionRequestType, cancellationToken);
				await writer.WriteAsync(SubscriptionRequestType.SnapshotPlusUpdates, cancellationToken);

				return FixMessages.RequestForPositions;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override OrderStates? GetOrderState(ExecutionReport report)
	{
		if (report.OrdStatus == OrdStatus.New)
		{
			return report.Text.IsEmpty() ? OrderStates.Pending : OrderStates.Active;
		}

		return base.GetOrderState(report);
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> ProcessExecutionReportAsync(ExecutionReport report, Func<ExecutionReport, ExecutionMessage, CancellationToken, IAsyncEnumerable<ExecutionMessage>> processExecMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (report.Text == "No standing orders in response to a OrderMassStatusRequest")
			yield break;

		await foreach (var msg in base.ProcessExecutionReportAsync(report, processExecMsg, cancellationToken))
			yield return msg;
	}

	/// <inheritdoc />
	protected override IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, CancellationToken cancellationToken)
	{
		return msgType switch
		{
			FixMessages.MassQuote => ReadMassQuoteAsync(reader, cancellationToken),
			_ => base.OnReadAsync(reader, msgType, cancellationToken),
		};
	}

	private async IAsyncEnumerable<Message> ReadMassQuoteAsync(IFixReader reader, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		string quoteReqId = null;
		string quoteId = null;
		(decimal volStep, List<string> secCodes) curr = default;
		List<(decimal volStep, List<string> secCodes)> quotes = null;

		var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
		{
			switch (tag)
			{
				case FixTags.QuoteReqID:
					quoteReqId = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.QuoteID:
					quoteId = await reader.ReadStringAsync(cancellationToken);
					return true;
				case FixTags.NoQuoteSets:
					quotes = new(await reader.ReadIntAsync(cancellationToken));
					return true;
				case FixTags.QuoteSetID:
					if (quotes == null)
						throw new InvalidOperationException("quotes == null");

					curr = ((decimal)(await reader.ReadStringAsync(cancellationToken)).To<double>(), new List<string>());
					quotes.Add(curr);
					return true;
				case FixTags.TotNoQuoteEntries:
					await reader.ReadIntAsync(cancellationToken);
					return true;
				case FixTags.NoQuoteEntries:
					if (curr == default)
						throw new InvalidOperationException("curr == null");

					curr.secCodes.Capacity = await reader.ReadIntAsync(cancellationToken);
					return true;
				case FixTags.QuoteEntryID:
					if (curr == default)
						throw new InvalidOperationException("curr == null");

					curr.secCodes.Add(await reader.ReadStringAsync(cancellationToken));
					return true;
				default:
					return false;
			}
		}, cancellationToken);

		if (!isOk)
			yield break;

		long.TryParse(quoteReqId, out var transId);

		foreach (var (volStep, secCodes) in quotes)
		{
			foreach (var secCode in secCodes)
			{
				yield return new SecurityMessage
				{
					SecurityId = new SecurityId
					{
						SecurityCode = secCode.ToUpperInvariant(),
						BoardCode = ExchangeBoard ?? SecurityId.AssociatedBoardCode,
					},
					VolumeStep = volStep,
					OriginalTransactionId = transId,
					SecurityType = secCode.StartsWith("#") ? SecurityTypes.Commodity : SecurityTypes.Currency,
				};
			}
		}

		if (transId != 0)
		{
			yield return new SubscriptionFinishedMessage { OriginalTransactionId = transId };

			yield return new SwissMassQuoteCancelMessage
			{
				BackMode = MessageBackModes.Direct,
				QuoteReqId = quoteReqId,
				QuoteId = quoteId
			};
		}
	}
}