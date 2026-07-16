#include "ctp_bridge.h"

#include "ThostFtdcMdApi.h"
#include "ThostFtdcTraderApi.h"

#include <algorithm>
#include <atomic>
#include <charconv>
#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#else
#include <iconv.h>
#endif

namespace
{
	constexpr int32_t channel_market = 1;
	constexpr int32_t channel_trader = 2;
	constexpr int32_t state_disconnected = 0;
	constexpr int32_t state_connected = 1;
	constexpr int32_t state_authenticated = 2;
	constexpr int32_t state_ready = 3;
	constexpr int32_t state_failed = -1;

	template <size_t Size>
	void copy_text(char (&destination)[Size], const char* source)
	{
		const auto length = source == nullptr ? size_t{0} : std::min(std::strlen(source), Size - 1);
		if (length > 0)
			std::memcpy(destination, source, length);
		destination[length] = '\0';
		if (length + 1 < Size)
			std::memset(destination + length + 1, 0, Size - length - 1);
	}

	template <size_t Size>
	void copy_text(char (&destination)[Size], const std::string& source)
	{
		copy_text(destination, source.c_str());
	}

	std::string to_utf8(const char* source)
	{
		if (source == nullptr || source[0] == '\0')
			return {};

#if defined(_WIN32)
		const auto source_length = static_cast<int>(std::strlen(source));
		auto wide_length = MultiByteToWideChar(54936, 0, source, source_length, nullptr, 0);
		if (wide_length <= 0)
			wide_length = MultiByteToWideChar(936, 0, source, source_length, nullptr, 0);
		if (wide_length <= 0)
			return source;

		std::wstring wide(static_cast<size_t>(wide_length), L'\0');
		if (MultiByteToWideChar(54936, 0, source, source_length, wide.data(), wide_length) <= 0 &&
			MultiByteToWideChar(936, 0, source, source_length, wide.data(), wide_length) <= 0)
			return source;

		const auto utf8_length = WideCharToMultiByte(CP_UTF8, 0, wide.data(), wide_length, nullptr, 0, nullptr, nullptr);
		if (utf8_length <= 0)
			return source;

		std::string result(static_cast<size_t>(utf8_length), '\0');
		WideCharToMultiByte(CP_UTF8, 0, wide.data(), wide_length, result.data(), utf8_length, nullptr, nullptr);
		return result;
#else
		iconv_t converter = iconv_open("UTF-8", "GB18030");
		if (converter == reinterpret_cast<iconv_t>(-1))
			return source;

		auto input_left = std::strlen(source);
		std::string result(input_left * 4 + 1, '\0');
		auto output_left = result.size() - 1;
		char* input = const_cast<char*>(source);
		char* output = result.data();
		const auto converted = iconv(converter, &input, &input_left, &output, &output_left);
		iconv_close(converter);
		if (converted == static_cast<size_t>(-1))
			return source;
		result.resize(result.size() - output_left - 1);
		return result;
#endif
	}

	double number(double value)
	{
		return std::isfinite(value) && std::abs(value) < 1e100 ? value : 0.0;
	}

	CtpBridgeError make_error(const CThostFtdcRspInfoField* info, int32_t request_id = 0, const char* instrument_id = nullptr, const char* order_ref = nullptr)
	{
		CtpBridgeError result{};
		result.id = info == nullptr ? 0 : info->ErrorID;
		result.request_id = request_id;
		copy_text(result.instrument_id, instrument_id);
		copy_text(result.order_ref, order_ref);
		if (info != nullptr)
			copy_text(result.message, to_utf8(info->ErrorMsg));
		return result;
	}

	bool has_error(const CThostFtdcRspInfoField* info)
	{
		return info != nullptr && info->ErrorID != 0;
	}

	struct Context;

	class MarketSpi final : public CThostFtdcMdSpi
	{
	public:
		explicit MarketSpi(Context& owner) : _owner(owner) {}

		void OnFrontConnected() override;
		void OnFrontDisconnected(int reason) override;
		void OnRspUserLogin(CThostFtdcRspUserLoginField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspError(CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspSubMarketData(CThostFtdcSpecificInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspUnSubMarketData(CThostFtdcSpecificInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRtnDepthMarketData(CThostFtdcDepthMarketDataField* depth) override;

	private:
		Context& _owner;
	};

	class TraderSpi final : public CThostFtdcTraderSpi
	{
	public:
		explicit TraderSpi(Context& owner) : _owner(owner) {}

		void OnFrontConnected() override;
		void OnFrontDisconnected(int reason) override;
		void OnRspAuthenticate(CThostFtdcRspAuthenticateField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspUserLogin(CThostFtdcRspUserLoginField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspSettlementInfoConfirm(CThostFtdcSettlementInfoConfirmField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspError(CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspQryInstrument(CThostFtdcInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspOrderInsert(CThostFtdcInputOrderField* order, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnErrRtnOrderInsert(CThostFtdcInputOrderField* order, CThostFtdcRspInfoField* info) override;
		void OnRspOrderAction(CThostFtdcInputOrderActionField* action, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnErrRtnOrderAction(CThostFtdcOrderActionField* action, CThostFtdcRspInfoField* info) override;
		void OnRtnOrder(CThostFtdcOrderField* order) override;
		void OnRtnTrade(CThostFtdcTradeField* trade) override;
		void OnRspQryOrder(CThostFtdcOrderField* order, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspQryTrade(CThostFtdcTradeField* trade, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspQryInvestorPosition(CThostFtdcInvestorPositionField* position, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;
		void OnRspQryTradingAccount(CThostFtdcTradingAccountField* account, CThostFtdcRspInfoField* info, int request_id, bool is_last) override;

	private:
		Context& _owner;
	};

	struct Context
	{
		explicit Context(const CtpBridgeCallbacks& callback_set, void* callback_user)
			: callbacks(callback_set), user_data(callback_user), market_spi(std::make_unique<MarketSpi>(*this)), trader_spi(std::make_unique<TraderSpi>(*this))
		{
		}

		CtpBridgeCallbacks callbacks{};
		void* user_data{};
		CThostFtdcMdApi* market{};
		CThostFtdcTraderApi* trader{};
		std::unique_ptr<MarketSpi> market_spi;
		std::unique_ptr<TraderSpi> trader_spi;
		std::mutex lifecycle_mutex;
		std::atomic<bool> stopping{false};
		std::atomic<int32_t> internal_request{1000000000};
		std::atomic<int64_t> order_reference{0};
		std::atomic<int32_t> action_reference{0};
		std::atomic<int32_t> front_id{0};
		std::atomic<int32_t> session_id{0};
		std::string broker_id;
		std::string user_id;
		std::string investor_id;
		std::string password;
		std::string app_id;
		std::string auth_code;
		std::string product_info;

		int32_t next_request()
		{
			return internal_request.fetch_add(1) + 1;
		}

		void notify_state(int32_t channel, int32_t state, int32_t reason = 0, const CtpBridgeError* error = nullptr) const
		{
			if (!stopping.load() && callbacks.state != nullptr)
				callbacks.state(channel, state, reason, error, user_data);
		}

		void notify_error(int32_t channel, const CtpBridgeError& error) const
		{
			if (!stopping.load() && callbacks.error != nullptr)
				callbacks.error(channel, &error, user_data);
		}

		void fail(int32_t channel, const CThostFtdcRspInfoField* info, int32_t request_id, int32_t fallback, const char* message)
		{
			auto error = make_error(info, request_id);
			if (error.id == 0)
			{
				error.id = fallback;
				copy_text(error.message, message);
			}
			notify_error(channel, error);
			notify_state(channel, state_failed, 0, &error);
		}

		int32_t request_market_login()
		{
			if (market == nullptr)
				return -100;
			CThostFtdcReqUserLoginField request{};
			copy_text(request.BrokerID, broker_id);
			copy_text(request.UserID, user_id);
			copy_text(request.Password, password);
			copy_text(request.UserProductInfo, product_info);
			return market->ReqUserLogin(&request, next_request());
		}

		int32_t request_authenticate()
		{
			if (trader == nullptr)
				return -100;
			CThostFtdcReqAuthenticateField request{};
			copy_text(request.BrokerID, broker_id);
			copy_text(request.UserID, user_id);
			copy_text(request.UserProductInfo, product_info);
			copy_text(request.AppID, app_id);
			copy_text(request.AuthCode, auth_code);
			return trader->ReqAuthenticate(&request, next_request());
		}

		int32_t request_trader_login()
		{
			if (trader == nullptr)
				return -100;
			CThostFtdcReqUserLoginField request{};
			copy_text(request.BrokerID, broker_id);
			copy_text(request.UserID, user_id);
			copy_text(request.Password, password);
			copy_text(request.UserProductInfo, product_info);
			return trader->ReqUserLogin(&request, next_request());
		}

		int32_t request_settlement_confirmation()
		{
			if (trader == nullptr)
				return -100;
			CThostFtdcSettlementInfoConfirmField request{};
			copy_text(request.BrokerID, broker_id);
			copy_text(request.InvestorID, investor_id);
			return trader->ReqSettlementInfoConfirm(&request, next_request());
		}
	};

	CtpBridgeInstrument convert(const CThostFtdcInstrumentField& source)
	{
		CtpBridgeInstrument target{};
		copy_text(target.instrument_id, source.InstrumentID);
		copy_text(target.exchange_id, source.ExchangeID);
		copy_text(target.exchange_instrument_id, source.ExchangeInstID);
		copy_text(target.product_id, source.ProductID);
		copy_text(target.underlying_instrument_id, source.UnderlyingInstrID);
		copy_text(target.name, to_utf8(source.InstrumentName));
		copy_text(target.open_date, source.OpenDate);
		copy_text(target.expire_date, source.ExpireDate);
		target.product_class = static_cast<unsigned char>(source.ProductClass);
		target.delivery_year = source.DeliveryYear;
		target.delivery_month = source.DeliveryMonth;
		target.max_market_order_volume = source.MaxMarketOrderVolume;
		target.min_market_order_volume = source.MinMarketOrderVolume;
		target.max_limit_order_volume = source.MaxLimitOrderVolume;
		target.min_limit_order_volume = source.MinLimitOrderVolume;
		target.volume_multiple = source.VolumeMultiple;
		target.price_tick = number(source.PriceTick);
		target.strike_price = number(source.StrikePrice);
		target.options_type = static_cast<unsigned char>(source.OptionsType);
		target.is_trading = source.IsTrading;
		target.life_phase = static_cast<unsigned char>(source.InstLifePhase);
		return target;
	}

	CtpBridgeDepth convert(const CThostFtdcDepthMarketDataField& source)
	{
		CtpBridgeDepth target{};
		copy_text(target.instrument_id, source.InstrumentID);
		copy_text(target.exchange_id, source.ExchangeID);
		copy_text(target.trading_day, source.TradingDay);
		copy_text(target.action_day, source.ActionDay);
		copy_text(target.update_time, source.UpdateTime);
		target.update_millisec = source.UpdateMillisec;
		target.last_price = number(source.LastPrice);
		target.pre_settlement_price = number(source.PreSettlementPrice);
		target.pre_close_price = number(source.PreClosePrice);
		target.pre_open_interest = number(source.PreOpenInterest);
		target.open_price = number(source.OpenPrice);
		target.high_price = number(source.HighestPrice);
		target.low_price = number(source.LowestPrice);
		target.volume = source.Volume;
		target.turnover = number(source.Turnover);
		target.open_interest = number(source.OpenInterest);
		target.close_price = number(source.ClosePrice);
		target.settlement_price = number(source.SettlementPrice);
		target.upper_limit_price = number(source.UpperLimitPrice);
		target.lower_limit_price = number(source.LowerLimitPrice);
		target.average_price = number(source.AveragePrice);
		const double bid_prices[] = {source.BidPrice1, source.BidPrice2, source.BidPrice3, source.BidPrice4, source.BidPrice5};
		const double ask_prices[] = {source.AskPrice1, source.AskPrice2, source.AskPrice3, source.AskPrice4, source.AskPrice5};
		const int32_t bid_volumes[] = {source.BidVolume1, source.BidVolume2, source.BidVolume3, source.BidVolume4, source.BidVolume5};
		const int32_t ask_volumes[] = {source.AskVolume1, source.AskVolume2, source.AskVolume3, source.AskVolume4, source.AskVolume5};
		for (size_t index = 0; index < 5; ++index)
		{
			target.bid_prices[index] = number(bid_prices[index]);
			target.ask_prices[index] = number(ask_prices[index]);
			target.bid_volumes[index] = bid_volumes[index];
			target.ask_volumes[index] = ask_volumes[index];
		}
		return target;
	}

	CtpBridgeOrder convert(const CThostFtdcOrderField& source)
	{
		CtpBridgeOrder target{};
		copy_text(target.instrument_id, source.InstrumentID);
		copy_text(target.exchange_id, source.ExchangeID);
		copy_text(target.exchange_instrument_id, source.ExchangeInstID);
		copy_text(target.order_ref, source.OrderRef);
		copy_text(target.order_system_id, source.OrderSysID);
		copy_text(target.order_local_id, source.OrderLocalID);
		copy_text(target.trading_day, source.TradingDay);
		copy_text(target.insert_date, source.InsertDate);
		copy_text(target.insert_time, source.InsertTime);
		copy_text(target.update_time, source.UpdateTime);
		copy_text(target.cancel_time, source.CancelTime);
		copy_text(target.status_message, to_utf8(source.StatusMsg));
		target.request_id = source.RequestID;
		target.front_id = source.FrontID;
		target.session_id = source.SessionID;
		target.price_type = static_cast<unsigned char>(source.OrderPriceType);
		target.direction = static_cast<unsigned char>(source.Direction);
		target.offset_flag = static_cast<unsigned char>(source.CombOffsetFlag[0]);
		target.hedge_flag = static_cast<unsigned char>(source.CombHedgeFlag[0]);
		target.limit_price = number(source.LimitPrice);
		target.stop_price = number(source.StopPrice);
		target.volume_original = source.VolumeTotalOriginal;
		target.volume_traded = source.VolumeTraded;
		target.volume_left = source.VolumeTotal;
		target.time_condition = static_cast<unsigned char>(source.TimeCondition);
		target.volume_condition = static_cast<unsigned char>(source.VolumeCondition);
		target.contingent_condition = static_cast<unsigned char>(source.ContingentCondition);
		target.submit_status = static_cast<unsigned char>(source.OrderSubmitStatus);
		target.order_status = static_cast<unsigned char>(source.OrderStatus);
		return target;
	}

	CtpBridgeTrade convert(const CThostFtdcTradeField& source)
	{
		CtpBridgeTrade target{};
		copy_text(target.instrument_id, source.InstrumentID);
		copy_text(target.exchange_id, source.ExchangeID);
		copy_text(target.exchange_instrument_id, source.ExchangeInstID);
		copy_text(target.order_ref, source.OrderRef);
		copy_text(target.order_system_id, source.OrderSysID);
		copy_text(target.trade_id, source.TradeID);
		copy_text(target.trading_day, source.TradingDay);
		copy_text(target.trade_date, source.TradeDate);
		copy_text(target.trade_time, source.TradeTime);
		target.direction = static_cast<unsigned char>(source.Direction);
		target.offset_flag = static_cast<unsigned char>(source.OffsetFlag);
		target.hedge_flag = static_cast<unsigned char>(source.HedgeFlag);
		target.price = number(source.Price);
		target.volume = source.Volume;
		target.sequence_number = source.SequenceNo;
		return target;
	}

	CtpBridgePosition convert(const CThostFtdcInvestorPositionField& source)
	{
		CtpBridgePosition target{};
		copy_text(target.instrument_id, source.InstrumentID);
		copy_text(target.exchange_id, source.ExchangeID);
		copy_text(target.trading_day, source.TradingDay);
		target.direction = static_cast<unsigned char>(source.PosiDirection);
		target.hedge_flag = static_cast<unsigned char>(source.HedgeFlag);
		target.position_date = static_cast<unsigned char>(source.PositionDate);
		target.position = source.Position;
		target.today_position = source.TodayPosition;
		target.yesterday_position = source.YdPosition;
		target.long_frozen = source.LongFrozen;
		target.short_frozen = source.ShortFrozen;
		target.position_cost = number(source.PositionCost);
		target.open_cost = number(source.OpenCost);
		target.use_margin = number(source.UseMargin);
		target.commission = number(source.Commission);
		target.close_profit = number(source.CloseProfit);
		target.position_profit = number(source.PositionProfit);
		target.settlement_price = number(source.SettlementPrice);
		target.option_value = number(source.OptionValue);
		return target;
	}

	CtpBridgeAccount convert(const CThostFtdcTradingAccountField& source)
	{
		CtpBridgeAccount target{};
		copy_text(target.account_id, source.AccountID);
		copy_text(target.currency_id, source.CurrencyID);
		copy_text(target.trading_day, source.TradingDay);
		target.pre_balance = number(source.PreBalance);
		target.balance = number(source.Balance);
		target.available = number(source.Available);
		target.withdraw_quota = number(source.WithdrawQuota);
		target.deposit = number(source.Deposit);
		target.withdraw = number(source.Withdraw);
		target.frozen_margin = number(source.FrozenMargin);
		target.frozen_cash = number(source.FrozenCash);
		target.frozen_commission = number(source.FrozenCommission);
		target.current_margin = number(source.CurrMargin);
		target.commission = number(source.Commission);
		target.close_profit = number(source.CloseProfit);
		target.position_profit = number(source.PositionProfit);
		target.option_value = number(source.OptionValue);
		return target;
	}

	void MarketSpi::OnFrontConnected()
	{
		_owner.notify_state(channel_market, state_connected);
		const auto result = _owner.request_market_login();
		if (result != 0)
			_owner.fail(channel_market, nullptr, 0, result, "CTP market login request was rejected locally.");
	}

	void MarketSpi::OnFrontDisconnected(int reason)
	{
		_owner.notify_state(channel_market, state_disconnected, reason);
	}

	void MarketSpi::OnRspUserLogin(CThostFtdcRspUserLoginField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)response;
		(void)is_last;
		if (has_error(info))
		{
			_owner.fail(channel_market, info, request_id, -101, "CTP market login failed.");
			return;
		}
		_owner.notify_state(channel_market, state_ready);
	}

	void MarketSpi::OnRspError(CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)is_last;
		_owner.notify_error(channel_market, make_error(info, request_id));
	}

	void MarketSpi::OnRspSubMarketData(CThostFtdcSpecificInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)is_last;
		if (has_error(info))
			_owner.notify_error(channel_market, make_error(info, request_id, instrument == nullptr ? nullptr : instrument->InstrumentID));
	}

	void MarketSpi::OnRspUnSubMarketData(CThostFtdcSpecificInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		OnRspSubMarketData(instrument, info, request_id, is_last);
	}

	void MarketSpi::OnRtnDepthMarketData(CThostFtdcDepthMarketDataField* depth)
	{
		if (depth != nullptr && !_owner.stopping.load() && _owner.callbacks.depth != nullptr)
		{
			const auto value = convert(*depth);
			_owner.callbacks.depth(&value, _owner.user_data);
		}
	}

	void TraderSpi::OnFrontConnected()
	{
		_owner.notify_state(channel_trader, state_connected);
		const auto result = _owner.app_id.empty() && _owner.auth_code.empty()
			? _owner.request_trader_login()
			: _owner.request_authenticate();
		if (result != 0)
			_owner.fail(channel_trader, nullptr, 0, result, "CTP authentication or login request was rejected locally.");
	}

	void TraderSpi::OnFrontDisconnected(int reason)
	{
		_owner.notify_state(channel_trader, state_disconnected, reason);
	}

	void TraderSpi::OnRspAuthenticate(CThostFtdcRspAuthenticateField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)response;
		(void)is_last;
		if (has_error(info))
		{
			_owner.fail(channel_trader, info, request_id, -102, "CTP client authentication failed.");
			return;
		}
		_owner.notify_state(channel_trader, state_authenticated);
		const auto result = _owner.request_trader_login();
		if (result != 0)
			_owner.fail(channel_trader, nullptr, request_id, result, "CTP trader login request was rejected locally.");
	}

	void TraderSpi::OnRspUserLogin(CThostFtdcRspUserLoginField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)is_last;
		if (has_error(info) || response == nullptr)
		{
			_owner.fail(channel_trader, info, request_id, -103, "CTP trader login failed.");
			return;
		}
		_owner.front_id.store(response->FrontID);
		_owner.session_id.store(response->SessionID);
		int64_t max_reference{};
		const auto end = response->MaxOrderRef + std::strlen(response->MaxOrderRef);
		const auto parsed = std::from_chars(response->MaxOrderRef, end, max_reference);
		if (parsed.ec == std::errc{})
			_owner.order_reference.store(max_reference);
		const auto result = _owner.request_settlement_confirmation();
		if (result != 0)
			_owner.fail(channel_trader, nullptr, request_id, result, "CTP settlement confirmation request was rejected locally.");
	}

	void TraderSpi::OnRspSettlementInfoConfirm(CThostFtdcSettlementInfoConfirmField* response, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)response;
		(void)is_last;
		if (has_error(info))
		{
			_owner.fail(channel_trader, info, request_id, -104, "CTP settlement confirmation failed.");
			return;
		}
		_owner.notify_state(channel_trader, state_ready);
	}

	void TraderSpi::OnRspError(CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)is_last;
		_owner.notify_error(channel_trader, make_error(info, request_id));
	}

	void TraderSpi::OnRspQryInstrument(CThostFtdcInstrumentField* instrument, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (_owner.stopping.load() || _owner.callbacks.instrument == nullptr)
			return;
		const auto error = make_error(info, request_id);
		const auto value = instrument == nullptr ? CtpBridgeInstrument{} : convert(*instrument);
		_owner.callbacks.instrument(instrument == nullptr ? nullptr : &value, has_error(info) ? &error : nullptr, request_id, is_last ? 1 : 0, _owner.user_data);
	}

	void TraderSpi::OnRspOrderInsert(CThostFtdcInputOrderField* order, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (!has_error(info))
			return;
		const auto error = make_error(info, request_id, order == nullptr ? nullptr : order->InstrumentID, order == nullptr ? nullptr : order->OrderRef);
		if (!_owner.stopping.load() && _owner.callbacks.order != nullptr)
			_owner.callbacks.order(nullptr, &error, request_id, is_last ? 1 : 0, 0, _owner.user_data);
	}

	void TraderSpi::OnErrRtnOrderInsert(CThostFtdcInputOrderField* order, CThostFtdcRspInfoField* info)
	{
		const auto request_id = order == nullptr ? 0 : order->RequestID;
		const auto error = make_error(info, request_id, order == nullptr ? nullptr : order->InstrumentID, order == nullptr ? nullptr : order->OrderRef);
		if (!_owner.stopping.load() && _owner.callbacks.order != nullptr)
			_owner.callbacks.order(nullptr, &error, request_id, 1, 0, _owner.user_data);
	}

	void TraderSpi::OnRspOrderAction(CThostFtdcInputOrderActionField* action, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		(void)is_last;
		if (has_error(info))
			_owner.notify_error(channel_trader, make_error(info, request_id, action == nullptr ? nullptr : action->InstrumentID, action == nullptr ? nullptr : action->OrderRef));
	}

	void TraderSpi::OnErrRtnOrderAction(CThostFtdcOrderActionField* action, CThostFtdcRspInfoField* info)
	{
		_owner.notify_error(channel_trader, make_error(info, action == nullptr ? 0 : action->RequestID, action == nullptr ? nullptr : action->InstrumentID, action == nullptr ? nullptr : action->OrderRef));
	}

	void TraderSpi::OnRtnOrder(CThostFtdcOrderField* order)
	{
		if (order != nullptr && !_owner.stopping.load() && _owner.callbacks.order != nullptr)
		{
			const auto value = convert(*order);
			_owner.callbacks.order(&value, nullptr, order->RequestID, 0, 0, _owner.user_data);
		}
	}

	void TraderSpi::OnRtnTrade(CThostFtdcTradeField* trade)
	{
		if (trade != nullptr && !_owner.stopping.load() && _owner.callbacks.trade != nullptr)
		{
			const auto value = convert(*trade);
			_owner.callbacks.trade(&value, nullptr, 0, 0, 0, _owner.user_data);
		}
	}

	void TraderSpi::OnRspQryOrder(CThostFtdcOrderField* order, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (_owner.stopping.load() || _owner.callbacks.order == nullptr)
			return;
		const auto error = make_error(info, request_id);
		const auto value = order == nullptr ? CtpBridgeOrder{} : convert(*order);
		_owner.callbacks.order(order == nullptr ? nullptr : &value, has_error(info) ? &error : nullptr, request_id, is_last ? 1 : 0, 1, _owner.user_data);
	}

	void TraderSpi::OnRspQryTrade(CThostFtdcTradeField* trade, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (_owner.stopping.load() || _owner.callbacks.trade == nullptr)
			return;
		const auto error = make_error(info, request_id);
		const auto value = trade == nullptr ? CtpBridgeTrade{} : convert(*trade);
		_owner.callbacks.trade(trade == nullptr ? nullptr : &value, has_error(info) ? &error : nullptr, request_id, is_last ? 1 : 0, 1, _owner.user_data);
	}

	void TraderSpi::OnRspQryInvestorPosition(CThostFtdcInvestorPositionField* position, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (_owner.stopping.load() || _owner.callbacks.position == nullptr)
			return;
		const auto error = make_error(info, request_id);
		const auto value = position == nullptr ? CtpBridgePosition{} : convert(*position);
		_owner.callbacks.position(position == nullptr ? nullptr : &value, has_error(info) ? &error : nullptr, request_id, is_last ? 1 : 0, _owner.user_data);
	}

	void TraderSpi::OnRspQryTradingAccount(CThostFtdcTradingAccountField* account, CThostFtdcRspInfoField* info, int request_id, bool is_last)
	{
		if (_owner.stopping.load() || _owner.callbacks.account == nullptr)
			return;
		const auto error = make_error(info, request_id);
		const auto value = account == nullptr ? CtpBridgeAccount{} : convert(*account);
		_owner.callbacks.account(account == nullptr ? nullptr : &value, has_error(info) ? &error : nullptr, request_id, is_last ? 1 : 0, _owner.user_data);
	}

	Context* as_context(void* context)
	{
		return static_cast<Context*>(context);
	}
}

extern "C"
{
	void* CTP_BRIDGE_CALL ctp_create(const CtpBridgeCallbacks* callbacks, void* user_data)
	{
		if (callbacks == nullptr)
			return nullptr;
		try
		{
			return new Context(*callbacks, user_data);
		}
		catch (...)
		{
			return nullptr;
		}
	}

	void CTP_BRIDGE_CALL ctp_destroy(void* context)
	{
		auto* owner = as_context(context);
		if (owner == nullptr)
			return;
		owner->stopping.store(true);
		{
			std::lock_guard<std::mutex> guard(owner->lifecycle_mutex);
			if (owner->market != nullptr)
			{
				owner->market->RegisterSpi(nullptr);
				owner->market->Release();
				owner->market = nullptr;
			}
			if (owner->trader != nullptr)
			{
				owner->trader->RegisterSpi(nullptr);
				owner->trader->Release();
				owner->trader = nullptr;
			}
		}
		delete owner;
	}

	int32_t CTP_BRIDGE_CALL ctp_connect_market(void* context, const char* front, const char* flow_path, const char* broker_id, const char* user_id, const char* password, int32_t production_mode)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || front == nullptr || front[0] == '\0')
			return -100;
		std::lock_guard<std::mutex> guard(owner->lifecycle_mutex);
		if (owner->market != nullptr)
			return -101;
		owner->broker_id = broker_id == nullptr ? "" : broker_id;
		owner->user_id = user_id == nullptr ? "" : user_id;
		owner->password = password == nullptr ? "" : password;
		owner->market = CThostFtdcMdApi::CreateFtdcMdApi(flow_path == nullptr ? "" : flow_path, false, false, production_mode != 0);
		if (owner->market == nullptr)
			return -102;
		owner->market->RegisterSpi(owner->market_spi.get());
		owner->market->RegisterFront(const_cast<char*>(front));
		owner->market->Init();
		return 0;
	}

	int32_t CTP_BRIDGE_CALL ctp_connect_trader(void* context, const char* front, const char* flow_path, const char* broker_id, const char* user_id, const char* investor_id, const char* password, const char* app_id, const char* auth_code, const char* product_info, int32_t resume_type, int32_t production_mode)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || front == nullptr || front[0] == '\0')
			return -100;
		std::lock_guard<std::mutex> guard(owner->lifecycle_mutex);
		if (owner->trader != nullptr)
			return -101;
		owner->broker_id = broker_id == nullptr ? "" : broker_id;
		owner->user_id = user_id == nullptr ? "" : user_id;
		owner->investor_id = investor_id == nullptr || investor_id[0] == '\0' ? owner->user_id : investor_id;
		owner->password = password == nullptr ? "" : password;
		owner->app_id = app_id == nullptr ? "" : app_id;
		owner->auth_code = auth_code == nullptr ? "" : auth_code;
		owner->product_info = product_info == nullptr ? "" : product_info;
		owner->trader = CThostFtdcTraderApi::CreateFtdcTraderApi(flow_path == nullptr ? "" : flow_path, production_mode != 0);
		if (owner->trader == nullptr)
			return -102;
		owner->trader->RegisterSpi(owner->trader_spi.get());
		const auto topic = static_cast<THOST_TE_RESUME_TYPE>(resume_type);
		owner->trader->SubscribePrivateTopic(topic);
		owner->trader->SubscribePublicTopic(topic);
		owner->trader->RegisterFront(const_cast<char*>(front));
		owner->trader->Init();
		return 0;
	}

	int32_t CTP_BRIDGE_CALL ctp_disconnect_market(void* context)
	{
		auto* owner = as_context(context);
		if (owner == nullptr)
			return -100;
		std::lock_guard<std::mutex> guard(owner->lifecycle_mutex);
		if (owner->market == nullptr)
			return 0;
		owner->market->RegisterSpi(nullptr);
		owner->market->Release();
		owner->market = nullptr;
		return 0;
	}

	int32_t CTP_BRIDGE_CALL ctp_disconnect_trader(void* context)
	{
		auto* owner = as_context(context);
		if (owner == nullptr)
			return -100;
		std::lock_guard<std::mutex> guard(owner->lifecycle_mutex);
		if (owner->trader == nullptr)
			return 0;
		owner->trader->RegisterSpi(nullptr);
		owner->trader->Release();
		owner->trader = nullptr;
		return 0;
	}

	int32_t CTP_BRIDGE_CALL ctp_subscribe_market_data(void* context, const char* instrument_id, int32_t subscribe)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->market == nullptr || instrument_id == nullptr || instrument_id[0] == '\0')
			return -100;
		char* instruments[] = {const_cast<char*>(instrument_id)};
		return subscribe != 0 ? owner->market->SubscribeMarketData(instruments, 1) : owner->market->UnSubscribeMarketData(instruments, 1);
	}

	int32_t CTP_BRIDGE_CALL ctp_next_order_ref(void* context, char* order_ref, int32_t capacity)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || order_ref == nullptr || capacity < 2)
			return -100;
		const auto value = owner->order_reference.fetch_add(1) + 1;
		const auto converted = std::to_chars(order_ref, order_ref + capacity - 1, value);
		if (converted.ec != std::errc{})
			return -101;
		*converted.ptr = '\0';
		return 0;
	}

	int32_t CTP_BRIDGE_CALL ctp_query_instruments(void* context, int32_t request_id, const char* exchange_id, const char* instrument_id, const char* product_id)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr)
			return -100;
		CThostFtdcQryInstrumentField request{};
		copy_text(request.ExchangeID, exchange_id);
		copy_text(request.InstrumentID, instrument_id);
		copy_text(request.ProductID, product_id);
		return owner->trader->ReqQryInstrument(&request, request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_insert_order(void* context, const CtpBridgeOrderRequest* request)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr || request == nullptr)
			return -100;
		CThostFtdcInputOrderField native{};
		copy_text(native.BrokerID, owner->broker_id);
		copy_text(native.InvestorID, owner->investor_id);
		copy_text(native.UserID, owner->user_id);
		copy_text(native.InstrumentID, request->instrument_id);
		copy_text(native.ExchangeID, request->exchange_id);
		copy_text(native.OrderRef, request->order_ref);
		native.RequestID = request->request_id;
		native.OrderPriceType = static_cast<TThostFtdcOrderPriceTypeType>(request->price_type);
		native.Direction = static_cast<TThostFtdcDirectionType>(request->direction);
		native.CombOffsetFlag[0] = static_cast<char>(request->offset_flag);
		native.CombHedgeFlag[0] = static_cast<char>(request->hedge_flag);
		native.LimitPrice = request->limit_price;
		native.VolumeTotalOriginal = request->volume;
		native.TimeCondition = static_cast<TThostFtdcTimeConditionType>(request->time_condition);
		copy_text(native.GTDDate, request->gtd_date);
		native.VolumeCondition = static_cast<TThostFtdcVolumeConditionType>(request->volume_condition);
		native.MinVolume = request->min_volume;
		native.ContingentCondition = static_cast<TThostFtdcContingentConditionType>(request->contingent_condition);
		native.StopPrice = request->stop_price;
		native.ForceCloseReason = static_cast<TThostFtdcForceCloseReasonType>(request->force_close_reason);
		native.IsAutoSuspend = request->auto_suspend;
		return owner->trader->ReqOrderInsert(&native, request->request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_cancel_order(void* context, const CtpBridgeCancelRequest* request)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr || request == nullptr)
			return -100;
		CThostFtdcInputOrderActionField native{};
		copy_text(native.BrokerID, owner->broker_id);
		copy_text(native.InvestorID, owner->investor_id);
		copy_text(native.UserID, owner->user_id);
		copy_text(native.InstrumentID, request->instrument_id);
		copy_text(native.ExchangeID, request->exchange_id);
		copy_text(native.OrderRef, request->order_ref);
		copy_text(native.OrderSysID, request->order_system_id);
		native.RequestID = request->request_id;
		native.OrderActionRef = request->action_ref > 0 ? request->action_ref : owner->action_reference.fetch_add(1) + 1;
		native.FrontID = request->front_id != 0 ? request->front_id : owner->front_id.load();
		native.SessionID = request->session_id != 0 ? request->session_id : owner->session_id.load();
		native.ActionFlag = THOST_FTDC_AF_Delete;
		return owner->trader->ReqOrderAction(&native, request->request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_query_orders(void* context, int32_t request_id)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr)
			return -100;
		CThostFtdcQryOrderField request{};
		copy_text(request.BrokerID, owner->broker_id);
		copy_text(request.InvestorID, owner->investor_id);
		return owner->trader->ReqQryOrder(&request, request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_query_trades(void* context, int32_t request_id)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr)
			return -100;
		CThostFtdcQryTradeField request{};
		copy_text(request.BrokerID, owner->broker_id);
		copy_text(request.InvestorID, owner->investor_id);
		return owner->trader->ReqQryTrade(&request, request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_query_positions(void* context, int32_t request_id, const char* instrument_id)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr)
			return -100;
		CThostFtdcQryInvestorPositionField request{};
		copy_text(request.BrokerID, owner->broker_id);
		copy_text(request.InvestorID, owner->investor_id);
		copy_text(request.InstrumentID, instrument_id);
		return owner->trader->ReqQryInvestorPosition(&request, request_id);
	}

	int32_t CTP_BRIDGE_CALL ctp_query_account(void* context, int32_t request_id, const char* currency_id)
	{
		auto* owner = as_context(context);
		if (owner == nullptr || owner->trader == nullptr)
			return -100;
		CThostFtdcQryTradingAccountField request{};
		copy_text(request.BrokerID, owner->broker_id);
		copy_text(request.InvestorID, owner->investor_id);
		copy_text(request.CurrencyID, currency_id);
		return owner->trader->ReqQryTradingAccount(&request, request_id);
	}

	const char* CTP_BRIDGE_CALL ctp_get_version(int32_t channel)
	{
		return channel == channel_market ? CThostFtdcMdApi::GetApiVersion() : CThostFtdcTraderApi::GetApiVersion();
	}

	int32_t CTP_BRIDGE_CALL ctp_get_struct_size(int32_t type)
	{
		switch (type)
		{
		case 1: return static_cast<int32_t>(sizeof(CtpBridgeError));
		case 2: return static_cast<int32_t>(sizeof(CtpBridgeInstrument));
		case 3: return static_cast<int32_t>(sizeof(CtpBridgeDepth));
		case 4: return static_cast<int32_t>(sizeof(CtpBridgeOrderRequest));
		case 5: return static_cast<int32_t>(sizeof(CtpBridgeCancelRequest));
		case 6: return static_cast<int32_t>(sizeof(CtpBridgeOrder));
		case 7: return static_cast<int32_t>(sizeof(CtpBridgeTrade));
		case 8: return static_cast<int32_t>(sizeof(CtpBridgePosition));
		case 9: return static_cast<int32_t>(sizeof(CtpBridgeAccount));
		default: return 0;
		}
	}
}
