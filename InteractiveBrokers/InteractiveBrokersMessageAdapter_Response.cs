namespace StockSharp.InteractiveBrokers;

using System.Text.RegularExpressions;

partial class InteractiveBrokersMessageAdapter
{
	private async ValueTask<bool> ProcessResponse(IBSocket socket, ResponseMessages message, CancellationToken cancellationToken)
	{
		switch (message)
		{
			case ResponseMessages.TickPrice:
			{
				await ReadTickPrice(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickVolume:
			{
				await ReadTickVolume(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.OrderStatus:
			{
				await ReadOrderStatus(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.ErrorMessage:
			{
				await ReadError(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.OpenOrder:
			{
				await ReadOpenOrder(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountValue:
			{
				await ReadAccountValue(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PortfolioValue:
			{
				await ReadPortfolioValue(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountUpdateTime:
			{
				await ReadAccountUpdateTime(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.NextOrderId:
			{
				await ReadNextOrderId(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SecurityInfo:
			{
				await ReadSecurityInfo(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MyTrade:
			{
				await ReadMyTrade(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MarketDepth:
			case ResponseMessages.MarketDepthL2:
			{
				await ReadMarketDepth(socket, message, cancellationToken);
				return true;
			}
			case ResponseMessages.NewsBulletins:
			{
				await ReadNewsBulletins(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.ManagedAccounts:
			{
				await ReadManagedAccounts(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.FinancialAdvice:
			{
				await ReadFinancialAdvice(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalData:
			{
				await ReadHistoricalData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.BondInfo:
			{
				await ReadBondInfo(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.ScannerParameters:
			{
				await ReadScannerParameters(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.ScannerData:
			{
				await ReadScannerData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickOptionComputation:
			{
				await ReadTickOptionComputation(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickGeneric:
			{
				await ReadTickGeneric(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickString:
			{
				await ReadTickString(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickEfp:
			{
				await ReadTickEfp(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.CurrentTime:
			{
				await ReadCurrentTime(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.RealTimeBars:
			{
				await ReadRealTimeBars(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.FundamentalData:
			{
				await ReadFundamentalData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SecurityInfoEnd:
			{
				await ReadSecurityInfoEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.OpenOrderEnd:
			{
				await ReadOpenOrderEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountDownloadEnd:
			{
				await ReadAccountDownloadEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MyTradeEnd:
			{
				await ReadMyTradeEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.DeltaNuetralValidation:
			{
				await ReadDeltaNuetralValidation(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickSnapshotEnd:
			{
				await ReadTickSnapshotEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MarketDataType:
			{
				await ReadMarketDataType(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.CommissionReport:
			{
				await ReadCommissionReport(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.Position:
			{
				await ReadPosition(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PositionEnd:
			{
				await ReadPositionEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountSummary:
			{
				await ReadAccountSummary(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountSummaryEnd:
			{
				await ReadAccountSummaryEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.VerifyMessageApi:
			{
				await ReadVerifyMessageApi(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.VerifyCompleted:
			{
				await ReadVerifyCompleted(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.DisplayGroupList:
			{
				await ReadDisplayGroupList(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.DisplayGroupUpdated:
			{
				await ReadDisplayGroupUpdated(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.VerifyAndAuthMessageApi:
			{
				await ReadVerifyAndAuthMessageApi(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.VerifyAndAuthCompleted:
			{
				await ReadVerifyAndAuthCompleted(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PositionMulti:
			{
				await ReadPositionMulti(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PositionMultiEnd:
			{
				await ReadPositionMultiEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountUpdateMulti:
			{
				await ReadAccountUpdateMulti(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.AccountUpdateMultiEnd:
			{
				await ReadAccountUpdateMultiEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SecurityDefinitionOptionParameter:
			{
				await ReadSecurityDefinitionOptionParameter(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SecurityDefinitionOptionParameterEnd:
			{
				await ReadSecurityDefinitionOptionParameterEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SoftDollarTier:
			{
				await ReadSoftDollarTier(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.FamilyCodes:
			{
				await ReadFamilyCodes(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SymbolSamples:
			{
				await ReadSymbolSamples(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MktDepthExchanges:
			{
				await ReadMktDepthExchanges(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickNews:
			{
				await ReadTickNews(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickReqParams:
			{
				await ReadTickReqParams(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.SmartComponents:
			{
				await ReadSmartComponents(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.NewsProviders:
			{
				await ReadNewsProviders(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.NewsArticle:
			{
				await ReadNewsArticle(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalNews:
			{
				await ReadHistoricalNews(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalNewsEnd:
			{
				await ReadHistoricalNewsEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HeadTimestamp:
			{
				await ReadHeadTimestamp(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistogramData:
			{
				await ReadHistogramData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalDataUpdate:
			{
				await ReadHistoricalDataUpdate(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.RerouteMktDataReq:
			{
				await ReadRerouteMktDataReq(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.RerouteMktDepthReq:
			{
				await ReadRerouteMktDepthReq(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.MarketRule:
			{
				await ReadMarketRule(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PnL:
			{
				await ReadPnL(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.PnLSingle:
			{
				await ReadPnLSingle(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalTick:
			{
				await ReadHistoricalTick(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalTickBidAsk:
			{
				await ReadHistoricalTickBidAsk(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalTickLast:
			{
				await ReadHistoricalTickLast(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.TickByTick:
			{
				await ReadTickByTick(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.OrderBound:
			{
				await ReadOrderBound(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.CompletedOrder:
			{
				await ReadCompletedOrder(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.CompletedOrdersEnd:
			{
				await ReadCompletedOrdersEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.ReplaceFAEnd:
			{
				await ReadReplaceFAEnd(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.WshMetaData:
			{
				await ReadWshMetaData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.WshEventData:
			{
				await ReadWshEventData(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.HistoricalSchedule:
			{
				await ReadHistoricalScheduleEvent(socket, cancellationToken);
				return true;
			}
			case ResponseMessages.UserInfo:
			{
				await ReadUserInfoEvent(socket, cancellationToken);
				return true;
			}
			default:
				return false;
		}
	}

	private async ValueTask ReadError(IBSocket socket, CancellationToken cancellationToken)
	{
		var version = await socket.ReadVersionAsync(cancellationToken);

		if (version < ServerVersions.V2)
		{
			await SendOutErrorAsync(await socket.ReadStringAsync(cancellationToken), cancellationToken);
		}
		else
		{
			var id = await socket.ReadLongAsync(cancellationToken);
			var code = await socket.ReadIntAsync(cancellationToken);
			var msg = await socket.ReadStringAsync(cancellationToken);

			if (socket.ServerVersion >= ServerVersions.EncodeMsgASCII7)
				msg = Regex.Unescape(msg);

			if (socket.ServerVersion >= ServerVersions.AdvancedOrderReject)
			{
				var tempStr = await socket.ReadStringAsync(cancellationToken);
				if (!tempStr.IsEmpty())
					msg += Environment.NewLine + Regex.Unescape(tempStr);
			}

			socket.AddInfoLog(() => msg);

			if (id == -1)
				return;

			var isOkCancel = (NotifyCodes)code == NotifyCodes.OrderCancelled;

			if (!isOkCancel && _messageRequests.TryGetValue(id, out var type))
			{
				await SendOutErrorAsync($"{msg} Number {id} Code {code}", cancellationToken);

				switch (type)
				{
					case MessageTypes.OrderRegister:
						await OnProcessOrderErrorAsync(id, msg, cancellationToken);
						break;

					case MessageTypes.OrderCancel:
						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Transactions,
							HasOrderInfo = true,
							OriginalTransactionId = GetTransactionId(id),
							OrderState = OrderStates.Failed,
							Error = new InvalidOperationException(msg)
						}, cancellationToken);
						break;

					case MessageTypes.OrderStatus:
					case MessageTypes.SecurityLookup:
					case MessageTypes.PortfolioLookup:
						await SendSubscriptionReplyAsync(id, cancellationToken, new InvalidOperationException(msg));
						break;

					default:
						await SendOutErrorAsync(LocalizedStrings.UnknownEvent.Put(type), cancellationToken);
						break;
				}
			}
			else
			{
				switch ((NotifyCodes)code)
				{
					case NotifyCodes.OrderCancelled:
					{
						OnProcessOrderCancelled(id);
						break;
					}
					case NotifyCodes.OrderCannotTransmit:
					case NotifyCodes.OrderCannotTransmitId:
					case NotifyCodes.OrderCannotTransmitIncomplete:
					case NotifyCodes.OrderDuplicateId:
					case NotifyCodes.OrderFilled:
					case NotifyCodes.OrderNotMatchPrev:
					case NotifyCodes.OrderPriceOutOfRange:
					case NotifyCodes.OrderSubmitFailed:
					case NotifyCodes.OrderVolumeTooSmall:
					case NotifyCodes.Rejected:
					{
						await OnProcessOrderErrorAsync(id, msg, cancellationToken);
						break;
					}
					case NotifyCodes.HistServiceError:
					{
						if (_mdCancellingRequests.Remove(id))
							break;

						var error = new InvalidOperationException($"{msg} Number {id} Code {code}");

						if (_mdRequests.Remove(id))
							await SendSubscriptionReplyAsync(id, cancellationToken, error);
						else
							await SendOutErrorAsync(error, cancellationToken);

						break;
					}
					default:
					{
						var error = new InvalidOperationException($"{msg} Number {id} Code {code}");

						if (_mdRequests.Remove(id))
							await SendSubscriptionReplyAsync(id, cancellationToken, error);
						else
							await SendOutErrorAsync(error, cancellationToken);

						break;
					}
				}
			}
		}
	}

	private async ValueTask ReadVerifyMessageApi(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var apiData = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.verifyMessageAPI(apiData);
	}

	private async ValueTask ReadVerifyCompleted(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var isSuccessful = (await socket.ReadStringAsync(cancellationToken)).EqualsIgnoreCase("true");
		var errorText = await socket.ReadStringAsync(cancellationToken);

		//if (isSuccessful)
		//    parent.startApi();
		// TODO: implement call to start api in client

		//eWrapper.verifyCompleted(isSuccessful, errorText);
	}

	private async ValueTask ReadDisplayGroupUpdated(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var contractInfo = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.displayGroupUpdated(requestId, contractInfo);
	}

	private async ValueTask ReadDisplayGroupList(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var groups = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.displayGroupList(requestId, groups);
	}

	private async ValueTask ReadVerifyAndAuthCompleted(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var isSuccessful = (await socket.ReadStringAsync(cancellationToken)).EqualsIgnoreCase("true");
		var errorText = await socket.ReadStringAsync(cancellationToken);

		//if (isSuccessful)
		//    parent.startApi();
		// TODO: implement call to start api in client

		//eWrapper.verifyAndAuthCompleted(isSuccessful, errorText);
	}

	private async ValueTask ReadVerifyAndAuthMessageApi(IBSocket socket, CancellationToken cancellationToken)
	{
		var msgVersion = await socket.ReadIntAsync(cancellationToken);
		var apiData = await socket.ReadStringAsync(cancellationToken);
		var xyzChallenge = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.verifyAndAuthMessageAPI(apiData, xyzChallenge);
	}

	private async ValueTask ReadCurrentTime(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);

		// http://www.interactivebrokers.com/en/software/api/apiguide/java/currenttime.htm
		var time = await socket.ReadUnixDateTimeAsync(cancellationToken);

		await OnProcessTimeShiftAsync(TimeHelper.NowWithOffset - time, cancellationToken);
	}
}