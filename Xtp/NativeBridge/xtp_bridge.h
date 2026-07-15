#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define XTP_BRIDGE_API __declspec(dllexport)
#define XTP_BRIDGE_CALL __cdecl
#else
#define XTP_BRIDGE_API __attribute__((visibility("default")))
#define XTP_BRIDGE_CALL
#endif

#pragma pack(push, 8)

typedef struct XtpBridgeError
{
	int32_t id;
	char message[124];
} XtpBridgeError;

typedef struct XtpBridgeSecurity
{
	int32_t exchange;
	char ticker[32];
	char name[128];
	int32_t security_type;
	double previous_close;
	double upper_limit;
	double lower_limit;
	double price_tick;
	int32_t buy_quantity_unit;
	int32_t sell_quantity_unit;
	int32_t status;
} XtpBridgeSecurity;

typedef struct XtpBridgeDepth
{
	int32_t exchange;
	char ticker[32];
	double last_price;
	double previous_close;
	double open_price;
	double high_price;
	double low_price;
	double close_price;
	double upper_limit;
	double lower_limit;
	int64_t time;
	int64_t volume;
	double turnover;
	double average_price;
	double bids[10];
	double asks[10];
	int64_t bid_volumes[10];
	int64_t ask_volumes[10];
	int64_t trades_count;
	char status[16];
} XtpBridgeDepth;

typedef struct XtpBridgeTick
{
	int32_t exchange;
	char ticker[32];
	int64_t sequence;
	int64_t time;
	int32_t type;
	int32_t channel;
	int64_t source_sequence;
	double price;
	int64_t volume;
	double turnover;
	int64_t bid_order_id;
	int64_t ask_order_id;
	int32_t flag;
} XtpBridgeTick;

typedef struct XtpBridgeOrderRequest
{
	uint32_t client_order_id;
	char ticker[32];
	int32_t market;
	double price;
	double stop_price;
	int64_t volume;
	int32_t price_type;
	int32_t side;
	int32_t position_effect;
	int32_t business_type;
} XtpBridgeOrderRequest;

typedef struct XtpBridgeOrder
{
	uint64_t order_id;
	uint32_t client_order_id;
	uint64_t cancel_order_id;
	char ticker[32];
	int32_t market;
	double price;
	int64_t volume;
	int32_t price_type;
	int32_t side;
	int32_t position_effect;
	int32_t business_type;
	int64_t traded_volume;
	int64_t balance;
	int64_t insert_time;
	int64_t update_time;
	int64_t cancel_time;
	double trade_amount;
	char local_order_id[32];
	int32_t status;
	int32_t submit_status;
	int32_t order_type;
} XtpBridgeOrder;

typedef struct XtpBridgeTrade
{
	uint64_t order_id;
	uint32_t client_order_id;
	char ticker[32];
	int32_t market;
	char trade_id[32];
	double price;
	int64_t volume;
	int64_t time;
	double amount;
	uint64_t report_index;
	int32_t side;
	int32_t position_effect;
	int32_t business_type;
} XtpBridgeTrade;

typedef struct XtpBridgePosition
{
	char ticker[32];
	char name[128];
	int32_t market;
	int64_t volume;
	int64_t sellable_volume;
	double average_price;
	double unrealized_pnl;
	int64_t yesterday_volume;
	int32_t direction;
	double market_value;
} XtpBridgePosition;

typedef struct XtpBridgeAsset
{
	double total_asset;
	double buying_power;
	double security_asset;
	double frozen_cash;
	double balance;
	double deposit_withdraw;
	double realized_pnl;
	int32_t account_type;
} XtpBridgeAsset;

typedef void (XTP_BRIDGE_CALL *XtpDisconnectedCallback)(int32_t channel, int32_t reason, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpErrorCallback)(int32_t channel, const XtpBridgeError* error, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpSecurityCallback)(const XtpBridgeSecurity* security, const XtpBridgeError* error, int32_t is_last, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpDepthCallback)(const XtpBridgeDepth* depth, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpTickCallback)(const XtpBridgeTick* tick, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpOrderCallback)(const XtpBridgeOrder* order, const XtpBridgeError* error, int32_t request_id, int32_t is_last, int32_t is_query, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpTradeCallback)(const XtpBridgeTrade* trade, const XtpBridgeError* error, int32_t request_id, int32_t is_last, int32_t is_query, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpCancelCallback)(uint64_t order_id, uint64_t cancel_order_id, const XtpBridgeError* error, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpPositionCallback)(const XtpBridgePosition* position, const XtpBridgeError* error, int32_t request_id, int32_t is_last, void* user_data);
typedef void (XTP_BRIDGE_CALL *XtpAssetCallback)(const XtpBridgeAsset* asset, const XtpBridgeError* error, int32_t request_id, int32_t is_last, void* user_data);

typedef struct XtpBridgeCallbacks
{
	XtpDisconnectedCallback disconnected;
	XtpErrorCallback error;
	XtpSecurityCallback security;
	XtpDepthCallback depth;
	XtpTickCallback tick;
	XtpOrderCallback order;
	XtpTradeCallback trade;
	XtpCancelCallback cancel;
	XtpPositionCallback position;
	XtpAssetCallback asset;
} XtpBridgeCallbacks;

#pragma pack(pop)

#ifdef __cplusplus
extern "C" {
#endif

XTP_BRIDGE_API void* XTP_BRIDGE_CALL xtp_create(uint8_t client_id, const char* data_path, const char* software_version, const char* software_key, const XtpBridgeCallbacks* callbacks, void* user_data);
XTP_BRIDGE_API void XTP_BRIDGE_CALL xtp_destroy(void* context);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_quote_login(void* context, const char* host, int32_t port, const char* user, const char* password, int32_t protocol, const char* local_ip);
XTP_BRIDGE_API uint64_t XTP_BRIDGE_CALL xtp_trader_login(void* context, const char* host, int32_t port, const char* user, const char* password, int32_t protocol, const char* local_ip);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_quote_logout(void* context);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_trader_logout(void* context);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_subscribe_market_data(void* context, const char* ticker, int32_t exchange, int32_t subscribe);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_subscribe_ticks(void* context, const char* ticker, int32_t exchange, int32_t subscribe);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_query_securities(void* context, int32_t exchange);
XTP_BRIDGE_API uint64_t XTP_BRIDGE_CALL xtp_insert_order(void* context, const XtpBridgeOrderRequest* request);
XTP_BRIDGE_API uint64_t XTP_BRIDGE_CALL xtp_cancel_order(void* context, uint64_t order_id);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_query_orders(void* context, int32_t request_id);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_query_trades(void* context, int32_t request_id);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_query_positions(void* context, int32_t request_id);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_query_assets(void* context, int32_t request_id);
XTP_BRIDGE_API int32_t XTP_BRIDGE_CALL xtp_get_last_error(void* context, int32_t channel, XtpBridgeError* error);
XTP_BRIDGE_API const char* XTP_BRIDGE_CALL xtp_get_version(void* context, int32_t channel);

#ifdef __cplusplus
}
#endif
