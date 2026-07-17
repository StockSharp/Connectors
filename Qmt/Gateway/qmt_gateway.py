#!/usr/bin/env python3
"""Local StockSharp gateway for the official XtQuant/MiniQMT Python API.

The gateway is intentionally a separate, user-managed process.  It never starts
MiniQMT and the StockSharp adapter never starts or embeds Python.
"""

from __future__ import annotations

import argparse
import asyncio
import dataclasses
import datetime as dt
import hmac
import importlib.metadata
import json
import logging
import math
import re
import signal
import struct
import sys
import threading
from dataclasses import dataclass
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Set, Tuple


PROTOCOL_VERSION = 1
GATEWAY_VERSION = "1.0.0"
MAX_FRAME_SIZE = 16 * 1024 * 1024
CLIENT_ORDER_PREFIX = "S#"
CHINA_TIME = dt.timezone(dt.timedelta(hours=8))


class GatewayFault(Exception):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


@dataclass(frozen=True)
class HelloRequest:
    token: str
    client: str


@dataclass(frozen=True)
class SearchRequest:
    query: str
    markets: List[str]
    limit: int


@dataclass(frozen=True)
class SecurityRequest:
    symbol: str


@dataclass(frozen=True)
class SubscriptionRequest:
    subscription_id: int
    symbol: str
    data_kind: str
    period: str


@dataclass(frozen=True)
class HistoryRequest:
    symbol: str
    period: str
    from_time: Optional[int]
    to_time: Optional[int]
    count: int


@dataclass(frozen=True)
class OrderRequest:
    client_order_id: int
    account_id: str
    symbol: str
    side: str
    order_type: str
    volume: int
    price: float


@dataclass(frozen=True)
class CancelRequest:
    client_order_id: int
    account_id: str
    order_id: int


@dataclass(frozen=True)
class IncomingEnvelope:
    version: int
    kind: str
    request_id: int
    payload: Mapping[str, Any]

    @staticmethod
    def parse(value: Any) -> "IncomingEnvelope":
        data = require_mapping(value, "envelope")
        version = require_int(data, "version")
        kind = require_string(data, "kind")
        request_id = require_int(data, "request_id")
        return IncomingEnvelope(version, kind, request_id, data)


@dataclass
class GatewayError:
    code: str
    message: str


@dataclass
class Hello:
    gateway_version: str
    xtquant_version: str
    account_id: str
    account_type: str


@dataclass
class Security:
    symbol: str
    code: str
    market: str
    name: str
    security_type: str
    currency: str = "CNY"
    price_step: Optional[float] = None
    volume_step: Optional[float] = None
    multiplier: Optional[float] = None
    expiry: Optional[int] = None
    is_trading: Optional[bool] = None


@dataclass
class PriceLevel:
    price: float
    volume: float


@dataclass
class Quote:
    symbol: str
    time: int
    last_price: Optional[float]
    open_price: Optional[float]
    high_price: Optional[float]
    low_price: Optional[float]
    previous_close: Optional[float]
    settlement_price: Optional[float]
    volume: Optional[float]
    turnover: Optional[float]
    open_interest: Optional[float]
    status: str
    bids: List[PriceLevel]
    asks: List[PriceLevel]


@dataclass
class MarketTrade:
    symbol: str
    trade_id: str
    time: int
    price: float
    volume: float
    side: str


@dataclass
class Candle:
    symbol: str
    period: str
    time: int
    open: float
    high: float
    low: float
    close: float
    volume: float
    turnover: Optional[float]
    open_interest: Optional[float]


@dataclass
class Account:
    account_id: str
    account_type: str


@dataclass
class Asset:
    account_id: str
    cash: float
    frozen_cash: float
    market_value: float
    total_asset: float


@dataclass
class Position:
    account_id: str
    symbol: str
    volume: float
    available_volume: float
    average_price: float
    market_value: float


@dataclass
class Order:
    account_id: str
    symbol: str
    order_id: int
    order_sys_id: str
    client_order_id: int
    time: int
    side: str
    order_type: str
    price: float
    volume: float
    filled_volume: float
    average_price: float
    status: int
    status_message: str


@dataclass
class Fill:
    account_id: str
    symbol: str
    order_id: int
    order_sys_id: str
    trade_id: str
    time: int
    side: str
    price: float
    volume: float


@dataclass
class Connection:
    is_connected: bool
    message: str


@dataclass
class SubscriptionResult:
    subscription_id: int
    native_id: int


@dataclass
class Envelope:
    version: int = PROTOCOL_VERSION
    kind: str = ""
    request_id: int = 0
    success: Optional[bool] = None
    error: Optional[GatewayError] = None
    hello: Optional[Hello] = None
    securities: Optional[List[Security]] = None
    subscription: Optional[SubscriptionResult] = None
    candles: Optional[List[Candle]] = None
    accounts: Optional[List[Account]] = None
    assets: Optional[List[Asset]] = None
    positions: Optional[List[Position]] = None
    orders: Optional[List[Order]] = None
    fills: Optional[List[Fill]] = None
    order_id: Optional[int] = None
    subscription_id: Optional[int] = None
    quote: Optional[Quote] = None
    trade: Optional[MarketTrade] = None
    candle: Optional[Candle] = None
    order: Optional[Order] = None
    fill: Optional[Fill] = None
    asset: Optional[Asset] = None
    position: Optional[Position] = None
    connection: Optional[Connection] = None


@dataclass
class Listener:
    session: "ClientSession"
    subscription_id: int
    data_kind: str


@dataclass
class NativeSubscription:
    symbol: str
    period: str
    native_id: int
    listeners: Dict[Tuple[int, int], Listener]


def require_mapping(value: Any, name: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise GatewayFault("invalid_request", f"{name} must be a JSON object.")
    return value


def require_payload(envelope: IncomingEnvelope, name: str) -> Mapping[str, Any]:
    return require_mapping(envelope.payload.get(name), name)


def require_string(data: Mapping[str, Any], name: str, allow_empty: bool = False) -> str:
    value = data.get(name)
    if not isinstance(value, str) or (not allow_empty and not value.strip()):
        raise GatewayFault("invalid_request", f"{name} must be a non-empty string.")
    return value.strip()


def optional_string(data: Mapping[str, Any], name: str) -> str:
    value = data.get(name)
    if value is None:
        return ""
    if not isinstance(value, str):
        raise GatewayFault("invalid_request", f"{name} must be a string.")
    return value.strip()


def require_int(data: Mapping[str, Any], name: str) -> int:
    value = data.get(name)
    if isinstance(value, bool) or not isinstance(value, int):
        raise GatewayFault("invalid_request", f"{name} must be an integer.")
    return value


def optional_int(data: Mapping[str, Any], name: str) -> Optional[int]:
    value = data.get(name)
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int):
        raise GatewayFault("invalid_request", f"{name} must be an integer.")
    return value


def require_number(data: Mapping[str, Any], name: str) -> float:
    value = data.get(name)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise GatewayFault("invalid_request", f"{name} must be numeric.")
    return float(value)


def string_list(data: Mapping[str, Any], name: str) -> List[str]:
    value = data.get(name)
    if value is None:
        return []
    if not isinstance(value, list) or any(not isinstance(item, str) for item in value):
        raise GatewayFault("invalid_request", f"{name} must be an array of strings.")
    return [item.strip().upper() for item in value if item.strip()]


def to_wire(value: Any) -> Any:
    if dataclasses.is_dataclass(value):
        result: Dict[str, Any] = {}
        for field in dataclasses.fields(value):
            item = getattr(value, field.name)
            if item is not None:
                result[field.name] = to_wire(item)
        return result
    if isinstance(value, (list, tuple)):
        return [to_wire(item) for item in value]
    if isinstance(value, Mapping):
        return {str(key): to_wire(item) for key, item in value.items()}
    return value


def native_value(value: Any, *names: str, default: Any = None) -> Any:
    for name in names:
        if isinstance(value, Mapping) and name in value:
            return value[name]
        getter = getattr(value, "get", None)
        if callable(getter):
            try:
                found = getter(name, None)
                if found is not None:
                    return found
            except (KeyError, TypeError, ValueError):
                pass
        if hasattr(value, name):
            return getattr(value, name)
    return default


def scalar(value: Any) -> Any:
    item = getattr(value, "item", None)
    if callable(item):
        try:
            return item()
        except (TypeError, ValueError):
            pass
    return value


def number(value: Any, default: float = 0.0) -> float:
    value = scalar(value)
    if value is None:
        return default
    try:
        result = float(value)
        return result if math.isfinite(result) else default
    except (TypeError, ValueError, OverflowError):
        return default


def optional_number(value: Any) -> Optional[float]:
    if value is None:
        return None
    result = number(value, math.nan)
    return result if math.isfinite(result) else None


def integer(value: Any, default: int = 0) -> int:
    value = scalar(value)
    try:
        return int(value)
    except (TypeError, ValueError, OverflowError):
        return default


def text(value: Any) -> str:
    value = scalar(value)
    return "" if value is None else str(value).strip()


def sequence(value: Any) -> List[Any]:
    if value is None:
        return []
    if isinstance(value, (str, bytes, bytearray)):
        return []
    if isinstance(value, Sequence):
        return list(value)
    converter = getattr(value, "tolist", None)
    if callable(converter):
        converted = converter()
        return converted if isinstance(converted, list) else [converted]
    try:
        return list(value)
    except TypeError:
        return [value]


def unix_milliseconds(value: Any) -> int:
    value = scalar(value)
    if value is None:
        return int(dt.datetime.now(dt.timezone.utc).timestamp() * 1000)
    timestamp = getattr(value, "timestamp", None)
    if callable(timestamp):
        try:
            return int(timestamp() * 1000)
        except (OSError, OverflowError, TypeError, ValueError):
            pass
    if isinstance(value, str):
        stripped = value.strip()
        for pattern in ("%Y%m%d%H%M%S", "%Y%m%d"):
            try:
                parsed = dt.datetime.strptime(stripped, pattern).replace(tzinfo=CHINA_TIME)
                return int(parsed.timestamp() * 1000)
            except ValueError:
                pass
    result = integer(value)
    if 0 < result < 100_000_000_000:
        result *= 1000
    return result


def xt_time(value: Optional[int]) -> str:
    if value is None:
        return ""
    return dt.datetime.fromtimestamp(value / 1000, CHINA_TIME).strftime("%Y%m%d%H%M%S")


def normalize_symbol(symbol: str) -> str:
    result = symbol.strip().upper()
    if "." not in result:
        raise GatewayFault("invalid_symbol", "XtQuant symbols must include .SH, .SZ, or .BJ.")
    code, market = result.rsplit(".", 1)
    if not code or market not in {"SH", "SZ", "BJ"}:
        raise GatewayFault("invalid_symbol", f"Unsupported XtQuant symbol {symbol}.")
    return f"{code}.{market}"


def parse_client_order_id(remark: Any) -> int:
    match = re.match(r"^S#(\d+)$", text(remark))
    return integer(match.group(1)) if match else 0


class ClientSession:
    def __init__(
        self,
        gateway: "QmtGateway",
        session_id: int,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        self.gateway = gateway
        self.session_id = session_id
        self.reader = reader
        self.writer = writer
        self.authenticated = False
        self.subscription_ids: Set[int] = set()
        self._send_lock = asyncio.Lock()

    async def run(self) -> None:
        peer = self.writer.get_extra_info("peername")
        logging.info("Client %s connected.", peer)
        try:
            while True:
                incoming: Optional[IncomingEnvelope] = None
                try:
                    incoming = IncomingEnvelope.parse(await self._read_frame())
                    response = await self.gateway.handle_request(self, incoming)
                except GatewayFault as fault:
                    response = Envelope(
                        kind=incoming.kind if incoming else "error",
                        request_id=incoming.request_id if incoming else 0,
                        success=False,
                        error=GatewayError(fault.code, str(fault)),
                    )
                except (asyncio.IncompleteReadError, ConnectionError):
                    break
                except Exception as error:
                    logging.exception("Unhandled client request error.")
                    response = Envelope(
                        kind=incoming.kind if incoming else "error",
                        request_id=incoming.request_id if incoming else 0,
                        success=False,
                        error=GatewayError("gateway_error", str(error)),
                    )

                await self.send(response)
                if response.error is not None and response.error.code == "authentication_failed":
                    break
        finally:
            await self.gateway.remove_session(self)
            self.writer.close()
            try:
                await self.writer.wait_closed()
            except (ConnectionError, RuntimeError):
                pass
            logging.info("Client %s disconnected.", peer)

    async def _read_frame(self) -> Any:
        header = await self.reader.readexactly(4)
        length = struct.unpack(">I", header)[0]
        if length <= 0 or length > MAX_FRAME_SIZE:
            raise GatewayFault("invalid_frame", f"Invalid frame length {length}.")
        payload = await self.reader.readexactly(length)
        try:
            return json.loads(payload.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise GatewayFault("invalid_json", str(error)) from error

    async def send(self, envelope: Envelope) -> None:
        payload = json.dumps(
            to_wire(envelope),
            ensure_ascii=False,
            allow_nan=False,
            separators=(",", ":"),
        ).encode("utf-8")
        if len(payload) > MAX_FRAME_SIZE:
            raise GatewayFault("frame_too_large", "Response exceeds 16 MiB.")
        async with self._send_lock:
            self.writer.write(struct.pack(">I", len(payload)))
            self.writer.write(payload)
            await self.writer.drain()


class QmtGateway:
    def __init__(self, arguments: argparse.Namespace) -> None:
        self.host: str = arguments.host
        self.port: int = arguments.port
        self.token: str = arguments.token
        self.qmt_path: str = arguments.qmt_path
        self.account_id: str = arguments.account
        self.account_type: str = arguments.account_type
        self.session_id: int = arguments.session_id
        self.quote_port: int = arguments.quote_port
        self.sectors: List[str] = [
            item.strip() for item in arguments.sectors.split(",") if item.strip()
        ]
        self.loop: Optional[asyncio.AbstractEventLoop] = None
        self.xtdata: Any = None
        self.xtconstant: Any = None
        self.trader: Any = None
        self.account: Any = None
        self.xtquant_version = "unknown"
        self._callback: Any = None
        self._sessions: Dict[int, ClientSession] = {}
        self._next_session_id = 0
        self._subscriptions: Dict[Tuple[str, str], NativeSubscription] = {}
        self._subscription_lock = asyncio.Lock()
        self._security_cache: Dict[str, Security] = {}
        self._universe: Optional[List[str]] = None
        self._native_connected = False
        self._reconnect_task: Optional[asyncio.Task[Any]] = None
        self._stopping = False
        self._native_state_lock = threading.RLock()

    async def start(self) -> None:
        self.loop = asyncio.get_running_loop()
        await asyncio.to_thread(self._start_native)
        server = await asyncio.start_server(self._accept, self.host, self.port)
        endpoints = ", ".join(str(sock.getsockname()) for sock in server.sockets or [])
        logging.info("QMT gateway %s listening on %s.", GATEWAY_VERSION, endpoints)
        async with server:
            await server.serve_forever()

    def _start_native(self) -> None:
        try:
            from xtquant import xtconstant, xtdata
            from xtquant.xttrader import XtQuantTrader, XtQuantTraderCallback
            from xtquant.xttype import StockAccount
        except ImportError as error:
            raise RuntimeError(
                "XtQuant is not installed. Install the package distributed with your MiniQMT version."
            ) from error

        try:
            self.xtquant_version = importlib.metadata.version("xtquant")
        except importlib.metadata.PackageNotFoundError:
            self.xtquant_version = text(getattr(sys.modules.get("xtquant"), "__version__", "unknown"))

        self.xtdata = xtdata
        self.xtconstant = xtconstant
        if self.quote_port > 0:
            xtdata.connect(port=self.quote_port)
        else:
            xtdata.connect()

        gateway = self

        class TraderCallback(XtQuantTraderCallback):
            def on_disconnected(self) -> None:
                gateway._trader_disconnected()

            def on_account_status(self, status: Any) -> None:
                gateway._account_status(status)

            def on_stock_order(self, order: Any) -> None:
                gateway._emit_from_thread(Envelope(kind="order", order=gateway._to_order(order)))

            def on_stock_trade(self, trade: Any) -> None:
                gateway._emit_from_thread(Envelope(kind="fill", fill=gateway._to_fill(trade)))

            def on_order_error(self, error: Any) -> None:
                gateway._native_error("order_error", error)

            def on_cancel_error(self, error: Any) -> None:
                gateway._native_error("cancel_error", error)

        self.account = StockAccount(self.account_id, self.account_type)
        self.trader = XtQuantTrader(self.qmt_path, self.session_id)
        self._callback = TraderCallback()
        self.trader.register_callback(self._callback)
        self.trader.start()
        self._connect_trader()

    def _connect_trader(self) -> None:
        with self._native_state_lock:
            result = self.trader.connect()
            if result not in (None, 0):
                raise RuntimeError(f"XtQuantTrader.connect failed with code {result}.")
            result = self.trader.subscribe(self.account)
            if result not in (None, 0):
                raise RuntimeError(f"XtQuantTrader.subscribe failed with code {result}.")
            self._native_connected = True

    async def stop(self) -> None:
        self._stopping = True
        if self._reconnect_task is not None:
            self._reconnect_task.cancel()
        sessions = list(self._sessions.values())
        for session in sessions:
            session.writer.close()
        async with self._subscription_lock:
            subscriptions = list(self._subscriptions.values())
            self._subscriptions.clear()
        for subscription in subscriptions:
            await asyncio.to_thread(self._unsubscribe_native, subscription.native_id)
        if self.trader is not None:
            await asyncio.to_thread(self._stop_trader)

    def _stop_trader(self) -> None:
        try:
            self.trader.stop()
        except Exception:
            logging.exception("Failed to stop XtQuantTrader.")

    async def _accept(
        self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        self._next_session_id += 1
        session = ClientSession(self, self._next_session_id, reader, writer)
        self._sessions[session.session_id] = session
        await session.run()

    async def handle_request(
        self, session: ClientSession, envelope: IncomingEnvelope
    ) -> Envelope:
        if envelope.version != PROTOCOL_VERSION:
            raise GatewayFault(
                "unsupported_version",
                f"Protocol version {envelope.version} is not supported.",
            )
        if envelope.request_id <= 0:
            raise GatewayFault("invalid_request", "request_id must be positive.")

        if envelope.kind == "hello":
            request = self._parse_hello(envelope)
            if session.authenticated:
                raise GatewayFault("invalid_state", "The session is already authenticated.")
            if not hmac.compare_digest(request.token, self.token):
                raise GatewayFault("authentication_failed", "Invalid gateway token.")
            session.authenticated = True
            return self._ok(
                envelope,
                hello=Hello(
                    GATEWAY_VERSION,
                    self.xtquant_version,
                    self.account_id,
                    self.account_type,
                ),
            )

        if not session.authenticated:
            raise GatewayFault("authentication_required", "Send hello before other requests.")
        if envelope.kind == "ping":
            return self._ok(envelope)
        if envelope.kind == "search":
            request = self._parse_search(envelope)
            return self._ok(envelope, securities=await self.search(request))
        if envelope.kind == "security":
            request = self._parse_security(envelope)
            security = await self.get_security(request.symbol)
            return self._ok(envelope, securities=[] if security is None else [security])
        if envelope.kind == "subscribe":
            request = self._parse_subscription(envelope)
            result = await self.subscribe(session, request)
            return self._ok(envelope, subscription=result)
        if envelope.kind == "unsubscribe":
            request = self._parse_subscription(envelope)
            await self.unsubscribe(session, request.subscription_id)
            return self._ok(envelope)
        if envelope.kind == "history":
            request = self._parse_history(envelope)
            return self._ok(envelope, candles=await self.history(request))
        if envelope.kind == "accounts":
            asset = await self.query_asset()
            return self._ok(
                envelope,
                accounts=[Account(self.account_id, self.account_type)],
                assets=[] if asset is None else [asset],
            )
        if envelope.kind == "positions":
            return self._ok(envelope, positions=await self.query_positions())
        if envelope.kind == "orders":
            return self._ok(envelope, orders=await self.query_orders())
        if envelope.kind == "fills":
            return self._ok(envelope, fills=await self.query_fills())
        if envelope.kind == "place_order":
            request = self._parse_order(envelope)
            return self._ok(envelope, order_id=await self.place_order(request))
        if envelope.kind == "cancel_order":
            request = self._parse_cancel(envelope)
            await self.cancel_order(request)
            return self._ok(envelope)
        raise GatewayFault("unsupported_request", f"Unsupported request kind {envelope.kind}.")

    @staticmethod
    def _ok(envelope: IncomingEnvelope, **values: Any) -> Envelope:
        return Envelope(
            kind=envelope.kind,
            request_id=envelope.request_id,
            success=True,
            **values,
        )

    @staticmethod
    def _parse_hello(envelope: IncomingEnvelope) -> HelloRequest:
        data = require_payload(envelope, "hello_request")
        return HelloRequest(require_string(data, "token"), optional_string(data, "client"))

    @staticmethod
    def _parse_search(envelope: IncomingEnvelope) -> SearchRequest:
        data = require_payload(envelope, "search_request")
        limit = require_int(data, "limit")
        if limit <= 0 or limit > 5000:
            raise GatewayFault("invalid_request", "limit must be between 1 and 5000.")
        return SearchRequest(optional_string(data, "query"), string_list(data, "markets"), limit)

    @staticmethod
    def _parse_security(envelope: IncomingEnvelope) -> SecurityRequest:
        data = require_payload(envelope, "security_request")
        return SecurityRequest(normalize_symbol(require_string(data, "symbol")))

    @staticmethod
    def _parse_subscription(envelope: IncomingEnvelope) -> SubscriptionRequest:
        data = require_payload(envelope, "subscription_request")
        request = SubscriptionRequest(
            require_int(data, "subscription_id"),
            normalize_symbol(require_string(data, "symbol")),
            require_string(data, "data_kind"),
            require_string(data, "period"),
        )
        if request.subscription_id <= 0:
            raise GatewayFault("invalid_request", "subscription_id must be positive.")
        if request.data_kind not in {"level1", "depth", "trade", "candle"}:
            raise GatewayFault("invalid_request", f"Unsupported data kind {request.data_kind}.")
        expected_period = {
            "level1": "tick",
            "depth": "tick",
            "trade": "l2transaction",
        }.get(request.data_kind)
        if expected_period is not None and request.period != expected_period:
            raise GatewayFault(
                "invalid_request",
                f"{request.data_kind} subscriptions require period {expected_period}.",
            )
        if request.data_kind == "candle" and request.period not in {
            "1m",
            "3m",
            "5m",
            "15m",
            "30m",
            "1h",
            "1d",
        }:
            raise GatewayFault("invalid_request", f"Unsupported candle period {request.period}.")
        return request

    @staticmethod
    def _parse_history(envelope: IncomingEnvelope) -> HistoryRequest:
        data = require_payload(envelope, "history_request")
        period = require_string(data, "period")
        if period not in {"1m", "3m", "5m", "15m", "30m", "1h", "1d"}:
            raise GatewayFault("invalid_request", f"Unsupported history period {period}.")
        count = require_int(data, "count")
        if count <= 0 or count > 10000:
            raise GatewayFault("invalid_request", "count must be between 1 and 10000.")
        return HistoryRequest(
            normalize_symbol(require_string(data, "symbol")),
            period,
            optional_int(data, "from"),
            optional_int(data, "to"),
            count,
        )

    @staticmethod
    def _parse_order(envelope: IncomingEnvelope) -> OrderRequest:
        data = require_payload(envelope, "order_request")
        request = OrderRequest(
            require_int(data, "client_order_id"),
            require_string(data, "account_id"),
            normalize_symbol(require_string(data, "symbol")),
            require_string(data, "side").lower(),
            require_string(data, "order_type").lower(),
            require_int(data, "volume"),
            require_number(data, "price"),
        )
        if request.client_order_id <= 0 or request.volume <= 0:
            raise GatewayFault("invalid_request", "Order identifiers and volume must be positive.")
        if request.side not in {"buy", "sell"}:
            raise GatewayFault("invalid_request", f"Unsupported order side {request.side}.")
        if request.order_type not in {"limit", "market"}:
            raise GatewayFault("invalid_request", f"Unsupported order type {request.order_type}.")
        if request.order_type == "limit" and request.price <= 0:
            raise GatewayFault("invalid_request", "Limit order price must be positive.")
        return request

    @staticmethod
    def _parse_cancel(envelope: IncomingEnvelope) -> CancelRequest:
        data = require_payload(envelope, "cancel_request")
        request = CancelRequest(
            require_int(data, "client_order_id"),
            require_string(data, "account_id"),
            require_int(data, "order_id"),
        )
        if request.client_order_id <= 0 or request.order_id <= 0:
            raise GatewayFault("invalid_request", "Order identifiers must be positive.")
        return request

    async def search(self, request: SearchRequest) -> List[Security]:
        symbols = await self._get_universe()
        markets = set(request.markets)
        query = request.query.upper()
        direct = self._direct_candidates(query) if query else []
        ordered = list(dict.fromkeys(direct + symbols))
        result: List[Security] = []
        is_code_query = not query or any(query in symbol for symbol in ordered[:100])

        for symbol in ordered:
            market = symbol.rsplit(".", 1)[-1]
            if markets and market not in markets:
                continue
            if query and is_code_query and query not in symbol:
                continue
            security = await self.get_security(symbol)
            if security is None:
                continue
            if query and query not in security.code.upper() and query not in security.name.upper():
                continue
            result.append(security)
            if len(result) >= request.limit:
                break
        return result

    async def get_security(self, symbol: str) -> Optional[Security]:
        symbol = normalize_symbol(symbol)
        cached = self._security_cache.get(symbol)
        if cached is not None:
            return cached
        detail = await asyncio.to_thread(self.xtdata.get_instrument_detail, symbol, True)
        if not detail:
            return None
        security = self._to_security(symbol, detail)
        self._security_cache[symbol] = security
        return security

    async def _get_universe(self) -> List[str]:
        if self._universe is None:
            self._universe = await asyncio.to_thread(self._load_universe)
        return self._universe

    def _load_universe(self) -> List[str]:
        result: Set[str] = set()
        for sector in self.sectors:
            try:
                for symbol in self.xtdata.get_stock_list_in_sector(sector) or []:
                    try:
                        result.add(normalize_symbol(text(symbol)))
                    except GatewayFault:
                        continue
            except Exception:
                logging.warning("XtQuant sector %s is unavailable.", sector, exc_info=True)
        if not result:
            raise RuntimeError(
                "No instruments were returned for the configured XtQuant sectors. "
                "Check --sectors and the MiniQMT data permission."
            )
        return sorted(result)

    @staticmethod
    def _direct_candidates(query: str) -> List[str]:
        if "." in query:
            try:
                return [normalize_symbol(query)]
            except GatewayFault:
                return []
        if not query.isdigit():
            return []
        preferred = (
            ["SH", "SZ", "BJ"]
            if query[0] in {"5", "6", "9"}
            else ["SZ", "SH", "BJ"]
        )
        return [f"{query}.{market}" for market in preferred]

    def _to_security(self, symbol: str, detail: Any) -> Security:
        code, market = symbol.rsplit(".", 1)
        instrument_type = self._security_type(symbol, detail)
        volume_step = optional_number(
            native_value(detail, "MinLimitOrderVolume", "minLimitOrderVolume")
        )
        if not volume_step and instrument_type == "stock":
            volume_step = 100.0
        return Security(
            symbol=symbol,
            code=code,
            market=market,
            name=text(
                native_value(
                    detail,
                    "InstrumentName",
                    "instrumentName",
                    "ProductName",
                    default=code,
                )
            )
            or code,
            security_type=instrument_type,
            price_step=optional_number(
                native_value(detail, "PriceTick", "priceTick", "PriceStep")
            ),
            volume_step=volume_step,
            multiplier=optional_number(
                native_value(detail, "VolumeMultiple", "volumeMultiple")
            ),
            expiry=self._expiry(
                native_value(detail, "ExpireDate", "expireDate", "MaturityDate")
            ),
            is_trading=self._is_trading(detail),
        )

    def _security_type(self, symbol: str, detail: Any) -> str:
        description = " ".join(
            [
                text(native_value(detail, "ProductType", "productType")),
                text(native_value(detail, "ProductID", "productID")),
                text(native_value(detail, "InstrumentType", "instrumentType")),
                text(native_value(detail, "ProductName", "productName")),
            ]
        ).lower()
        try:
            flags = self.xtdata.get_instrument_type(symbol)
            if isinstance(flags, Mapping):
                description += " " + " ".join(
                    str(key).lower() for key, value in flags.items() if bool(value)
                )
            else:
                description += " " + text(flags).lower()
        except Exception:
            pass
        for marker, security_type in (
            ("option", "option"),
            ("期权", "option"),
            ("future", "future"),
            ("期货", "future"),
            ("index", "index"),
            ("指数", "index"),
            ("bond", "bond"),
            ("债", "bond"),
            ("fund", "fund"),
            ("etf", "fund"),
            ("基金", "fund"),
        ):
            if marker in description:
                return security_type
        return "stock"

    @staticmethod
    def _expiry(value: Any) -> Optional[int]:
        value = text(value)
        if not value or value in {"0", "00000000"}:
            return None
        try:
            parsed = dt.datetime.strptime(value[:8], "%Y%m%d").replace(tzinfo=CHINA_TIME)
            return int(parsed.timestamp() * 1000)
        except ValueError:
            return None

    @staticmethod
    def _is_trading(detail: Any) -> Optional[bool]:
        value = native_value(detail, "IsTrading", "isTrading", "InstrumentStatus")
        if value is None:
            return None
        if isinstance(value, bool):
            return value
        return text(value).lower() not in {"0", "false", "closed", "suspended", "停牌"}

    async def history(self, request: HistoryRequest) -> List[Candle]:
        def load() -> Any:
            return self.xtdata.get_market_data_ex(
                field_list=[],
                stock_list=[request.symbol],
                period=request.period,
                start_time=xt_time(request.from_time),
                end_time=xt_time(request.to_time),
                count=request.count,
                dividend_type="none",
                fill_data=False,
            )

        payload = await asyncio.to_thread(load)
        frame = native_value(payload, request.symbol)
        if frame is None and isinstance(payload, Mapping) and payload:
            frame = next(iter(payload.values()))
        rows = self._frame_rows(frame)
        candles = [
            self._to_candle(request.symbol, request.period, row, index)
            for index, row in rows
        ]
        return sorted(
            (candle for candle in candles if candle.time > 0),
            key=lambda candle: candle.time,
        )[-request.count :]

    @staticmethod
    def _frame_rows(frame: Any) -> List[Tuple[Any, Any]]:
        if frame is None:
            return []
        iterator = getattr(frame, "iterrows", None)
        if callable(iterator):
            return list(iterator())
        if isinstance(frame, Mapping):
            return list(enumerate(frame.values()))
        return list(enumerate(sequence(frame)))

    async def subscribe(
        self, session: ClientSession, request: SubscriptionRequest
    ) -> SubscriptionResult:
        if request.subscription_id in session.subscription_ids:
            raise GatewayFault(
                "duplicate_subscription",
                f"Subscription {request.subscription_id} already exists.",
            )
        key = (request.symbol, request.period)
        async with self._subscription_lock:
            subscription = self._subscriptions.get(key)
            if subscription is None:
                callback = lambda payload, native_key=key: self._market_callback(
                    native_key, payload
                )

                def subscribe_native() -> int:
                    return integer(
                        self.xtdata.subscribe_quote(
                            request.symbol,
                            period=request.period,
                            count=0,
                            callback=callback,
                        )
                    )

                native_id = await asyncio.to_thread(subscribe_native)
                if native_id <= 0:
                    hint = (
                        " The l2transaction stream requires the XtQuant Level-2 entitlement."
                        if request.period == "l2transaction"
                        else ""
                    )
                    raise GatewayFault(
                        "subscription_failed",
                        f"XtQuant subscribe_quote failed for {request.symbol}/{request.period}.{hint}",
                    )
                subscription = NativeSubscription(
                    request.symbol, request.period, native_id, {}
                )
                self._subscriptions[key] = subscription
            listener_key = (session.session_id, request.subscription_id)
            subscription.listeners[listener_key] = Listener(
                session, request.subscription_id, request.data_kind
            )
            session.subscription_ids.add(request.subscription_id)

        if request.period == "tick":
            asyncio.create_task(
                self._send_initial_quote(session, request.subscription_id, request.data_kind, request.symbol)
            )
        return SubscriptionResult(request.subscription_id, subscription.native_id)

    async def unsubscribe(self, session: ClientSession, subscription_id: int) -> None:
        if subscription_id not in session.subscription_ids:
            return
        await self._remove_listener(session, subscription_id)

    async def remove_session(self, session: ClientSession) -> None:
        for subscription_id in list(session.subscription_ids):
            await self._remove_listener(session, subscription_id)
        self._sessions.pop(session.session_id, None)

    async def _remove_listener(
        self, session: ClientSession, subscription_id: int
    ) -> None:
        native_id = 0
        async with self._subscription_lock:
            listener_key = (session.session_id, subscription_id)
            empty_key: Optional[Tuple[str, str]] = None
            for key, subscription in self._subscriptions.items():
                if listener_key in subscription.listeners:
                    subscription.listeners.pop(listener_key, None)
                    if not subscription.listeners:
                        empty_key = key
                        native_id = subscription.native_id
                    break
            if empty_key is not None:
                self._subscriptions.pop(empty_key, None)
            session.subscription_ids.discard(subscription_id)
        if native_id > 0:
            await asyncio.to_thread(self._unsubscribe_native, native_id)

    def _unsubscribe_native(self, native_id: int) -> None:
        try:
            self.xtdata.unsubscribe_quote(native_id)
        except Exception:
            logging.exception("XtQuant unsubscribe_quote(%s) failed.", native_id)

    def _market_callback(self, key: Tuple[str, str], payload: Any) -> None:
        if self.loop is None or self.loop.is_closed():
            return
        asyncio.run_coroutine_threadsafe(self._dispatch_market(key, payload), self.loop)

    async def _dispatch_market(
        self, key: Tuple[str, str], payload: Any
    ) -> None:
        async with self._subscription_lock:
            subscription = self._subscriptions.get(key)
            if subscription is None:
                return
            listeners = list(subscription.listeners.values())
        rows = self._market_rows(payload, subscription.symbol)
        for row in rows:
            for listener in listeners:
                try:
                    if listener.data_kind in {"level1", "depth"}:
                        envelope = Envelope(
                            kind=listener.data_kind,
                            subscription_id=listener.subscription_id,
                            quote=self._to_quote(subscription.symbol, row),
                        )
                    elif listener.data_kind == "trade":
                        envelope = Envelope(
                            kind="trade",
                            subscription_id=listener.subscription_id,
                            trade=self._to_market_trade(subscription.symbol, row),
                        )
                    else:
                        envelope = Envelope(
                            kind="candle",
                            subscription_id=listener.subscription_id,
                            candle=self._to_candle(
                                subscription.symbol, subscription.period, row, None
                            ),
                        )
                    await listener.session.send(envelope)
                except (ConnectionError, RuntimeError):
                    pass
                except Exception:
                    logging.exception("Failed to dispatch XtQuant market data.")

    async def _send_initial_quote(
        self,
        session: ClientSession,
        subscription_id: int,
        data_kind: str,
        symbol: str,
    ) -> None:
        try:
            payload = await asyncio.to_thread(self.xtdata.get_full_tick, [symbol])
            row = native_value(payload, symbol)
            if row is None:
                return
            await session.send(
                Envelope(
                    kind=data_kind,
                    subscription_id=subscription_id,
                    quote=self._to_quote(symbol, row),
                )
            )
        except (ConnectionError, RuntimeError):
            pass
        except Exception:
            logging.exception("Failed to read initial XtQuant quote for %s.", symbol)

    @staticmethod
    def _market_rows(payload: Any, symbol: str) -> List[Any]:
        value = native_value(payload, symbol)
        if value is None and isinstance(payload, Mapping):
            if "time" in payload or "lastPrice" in payload:
                value = payload
            elif payload:
                value = next(iter(payload.values()))
        if isinstance(value, Mapping):
            return [value]
        return sequence(value)

    def _to_quote(self, symbol: str, row: Any) -> Quote:
        bid_prices = sequence(native_value(row, "bidPrice", "bid_price"))
        bid_volumes = sequence(native_value(row, "bidVol", "bidVolume", "bid_volume"))
        ask_prices = sequence(native_value(row, "askPrice", "ask_price"))
        ask_volumes = sequence(native_value(row, "askVol", "askVolume", "ask_volume"))
        bids = [
            PriceLevel(number(price), number(bid_volumes[index]))
            for index, price in enumerate(bid_prices)
            if index < len(bid_volumes) and number(price) > 0
        ]
        asks = [
            PriceLevel(number(price), number(ask_volumes[index]))
            for index, price in enumerate(ask_prices)
            if index < len(ask_volumes) and number(price) > 0
        ]
        return Quote(
            symbol=symbol,
            time=unix_milliseconds(native_value(row, "time")),
            last_price=optional_number(native_value(row, "lastPrice", "last_price")),
            open_price=optional_number(native_value(row, "open", "openPrice")),
            high_price=optional_number(native_value(row, "high", "highPrice")),
            low_price=optional_number(native_value(row, "low", "lowPrice")),
            previous_close=optional_number(
                native_value(row, "lastClose", "preClose", "previousClose")
            ),
            settlement_price=optional_number(
                native_value(row, "lastSettlementPrice", "settlementPrice")
            ),
            volume=optional_number(native_value(row, "volume")),
            turnover=optional_number(native_value(row, "amount", "turnover")),
            open_interest=optional_number(native_value(row, "openInt", "openInterest")),
            status=text(native_value(row, "stockStatus", "status")),
            bids=bids,
            asks=asks,
        )

    @staticmethod
    def _to_market_trade(symbol: str, row: Any) -> MarketTrade:
        flag = text(native_value(row, "tradeFlag", "tradeType", "side")).upper()
        side = "buy" if flag.startswith("B") else "sell" if flag.startswith("S") else ""
        return MarketTrade(
            symbol=symbol,
            trade_id=text(native_value(row, "tradeIndex", "trade_id", "index")),
            time=unix_milliseconds(native_value(row, "time")),
            price=number(native_value(row, "price")),
            volume=number(native_value(row, "volume")),
            side=side,
        )

    @staticmethod
    def _to_candle(symbol: str, period: str, row: Any, index: Any) -> Candle:
        time_value = native_value(row, "time", default=index)
        return Candle(
            symbol=symbol,
            period=period,
            time=unix_milliseconds(time_value),
            open=number(native_value(row, "open")),
            high=number(native_value(row, "high")),
            low=number(native_value(row, "low")),
            close=number(native_value(row, "close")),
            volume=number(native_value(row, "volume")),
            turnover=optional_number(native_value(row, "amount", "turnover")),
            open_interest=optional_number(
                native_value(row, "openInterest", "openInt")
            ),
        )

    async def query_asset(self) -> Optional[Asset]:
        native = await asyncio.to_thread(self.trader.query_stock_asset, self.account)
        return None if native is None else self._to_asset(native)

    async def query_positions(self) -> List[Position]:
        native = await asyncio.to_thread(self.trader.query_stock_positions, self.account)
        return [self._to_position(item) for item in native or []]

    async def query_orders(self) -> List[Order]:
        native = await asyncio.to_thread(
            self.trader.query_stock_orders, self.account, False
        )
        return [self._to_order(item) for item in native or []]

    async def query_fills(self) -> List[Fill]:
        native = await asyncio.to_thread(self.trader.query_stock_trades, self.account)
        return [self._to_fill(item) for item in native or []]

    async def place_order(self, request: OrderRequest) -> int:
        self._check_account(request.account_id)
        if not self._native_connected:
            raise GatewayFault("not_connected", "XtQuantTrader is not connected.")
        side = (
            self.xtconstant.STOCK_BUY
            if request.side == "buy"
            else self.xtconstant.STOCK_SELL
        )
        price_type = (
            self.xtconstant.FIX_PRICE
            if request.order_type == "limit"
            else self.xtconstant.LATEST_PRICE
        )
        remark = f"{CLIENT_ORDER_PREFIX}{request.client_order_id}"

        def place() -> int:
            return integer(
                self.trader.order_stock(
                    self.account,
                    request.symbol,
                    side,
                    request.volume,
                    price_type,
                    request.price if request.order_type == "limit" else 0,
                    "StockSharp",
                    remark,
                )
            )

        order_id = await asyncio.to_thread(place)
        if order_id <= 0:
            raise GatewayFault(
                "order_failed", f"XtQuantTrader.order_stock returned {order_id}."
            )
        return order_id

    async def cancel_order(self, request: CancelRequest) -> None:
        self._check_account(request.account_id)
        if not self._native_connected:
            raise GatewayFault("not_connected", "XtQuantTrader is not connected.")
        result = await asyncio.to_thread(
            self.trader.cancel_order_stock, self.account, request.order_id
        )
        if result not in (None, 0):
            raise GatewayFault(
                "cancel_failed",
                f"XtQuantTrader.cancel_order_stock returned {result}.",
            )

    def _check_account(self, account_id: str) -> None:
        if account_id != self.account_id:
            raise GatewayFault(
                "unknown_account",
                f"Gateway is configured for account {self.account_id}, not {account_id}.",
            )

    def _to_asset(self, value: Any) -> Asset:
        return Asset(
            account_id=text(native_value(value, "account_id", default=self.account_id)),
            cash=number(native_value(value, "cash")),
            frozen_cash=number(native_value(value, "frozen_cash")),
            market_value=number(native_value(value, "market_value")),
            total_asset=number(native_value(value, "total_asset")),
        )

    def _to_position(self, value: Any) -> Position:
        return Position(
            account_id=text(native_value(value, "account_id", default=self.account_id)),
            symbol=text(native_value(value, "stock_code")).upper(),
            volume=number(native_value(value, "volume")),
            available_volume=number(native_value(value, "can_use_volume")),
            average_price=number(native_value(value, "avg_price", "open_price")),
            market_value=number(native_value(value, "market_value")),
        )

    def _to_order(self, value: Any) -> Order:
        price = number(native_value(value, "price"))
        fixed_price = integer(getattr(self.xtconstant, "FIX_PRICE", -1))
        price_type = integer(native_value(value, "price_type"), -2)
        return Order(
            account_id=text(native_value(value, "account_id", default=self.account_id)),
            symbol=text(native_value(value, "stock_code")).upper(),
            order_id=integer(native_value(value, "order_id")),
            order_sys_id=text(native_value(value, "order_sysid", "order_sys_id")),
            client_order_id=parse_client_order_id(
                native_value(value, "order_remark", "remark")
            ),
            time=unix_milliseconds(native_value(value, "order_time", "time")),
            side=self._side(native_value(value, "order_type")),
            order_type="limit" if price_type == fixed_price or price > 0 else "market",
            price=price,
            volume=number(native_value(value, "order_volume", "volume")),
            filled_volume=number(native_value(value, "traded_volume", "filled_volume")),
            average_price=number(native_value(value, "traded_price", "average_price")),
            status=integer(native_value(value, "order_status", "status"), 255),
            status_message=text(native_value(value, "status_msg", "status_message")),
        )

    def _to_fill(self, value: Any) -> Fill:
        return Fill(
            account_id=text(native_value(value, "account_id", default=self.account_id)),
            symbol=text(native_value(value, "stock_code")).upper(),
            order_id=integer(native_value(value, "order_id")),
            order_sys_id=text(native_value(value, "order_sysid", "order_sys_id")),
            trade_id=text(native_value(value, "traded_id", "trade_id")),
            time=unix_milliseconds(native_value(value, "traded_time", "time")),
            side=self._side(native_value(value, "order_type")),
            price=number(native_value(value, "traded_price", "price")),
            volume=number(native_value(value, "traded_volume", "volume")),
        )

    def _side(self, value: Any) -> str:
        return (
            "sell"
            if integer(value) == integer(getattr(self.xtconstant, "STOCK_SELL", -1))
            else "buy"
        )

    def _trader_disconnected(self) -> None:
        self._native_connected = False
        self._emit_from_thread(
            Envelope(
                kind="connection",
                connection=Connection(False, "XtQuantTrader disconnected from MiniQMT."),
            )
        )
        if self.loop is not None and not self.loop.is_closed():
            self.loop.call_soon_threadsafe(self._start_reconnect)

    def _account_status(self, status: Any) -> None:
        status_code = integer(native_value(status, "status"), -1)
        ok_code = integer(getattr(self.xtconstant, "ACCOUNT_STATUS_OK", 0))
        connected = status_code == ok_code
        account_id = text(native_value(status, "account_id", default=self.account_id))
        self._emit_from_thread(
            Envelope(
                kind="connection",
                connection=Connection(
                    connected,
                    f"XtQuant account {account_id} status is {status_code}.",
                ),
            )
        )

    def _native_error(self, code: str, value: Any) -> None:
        order_id = native_value(value, "order_id", "order_id")
        message = text(
            native_value(
                value,
                "error_msg",
                "error_message",
                "error",
                default=value,
            )
        )
        if order_id is not None:
            message = f"Order {integer(order_id)}: {message}"
        self._emit_from_thread(
            Envelope(kind="error", error=GatewayError(code, message or code))
        )

    def _emit_from_thread(self, envelope: Envelope) -> None:
        if self.loop is None or self.loop.is_closed():
            return
        asyncio.run_coroutine_threadsafe(self._broadcast(envelope), self.loop)

    async def _broadcast(self, envelope: Envelope) -> None:
        for session in list(self._sessions.values()):
            if not session.authenticated:
                continue
            try:
                await session.send(envelope)
            except (ConnectionError, RuntimeError):
                pass

    def _start_reconnect(self) -> None:
        if self._stopping:
            return
        if self._reconnect_task is None or self._reconnect_task.done():
            self._reconnect_task = asyncio.create_task(self._reconnect())

    async def _reconnect(self) -> None:
        attempt = 0
        while not self._stopping and not self._native_connected:
            attempt += 1
            try:
                await asyncio.to_thread(self._connect_trader)
                await self._restore_native_subscriptions()
                await self._broadcast(
                    Envelope(
                        kind="connection",
                        connection=Connection(True, "XtQuantTrader reconnected."),
                    )
                )
                return
            except Exception as error:
                logging.warning(
                    "XtQuantTrader reconnect attempt %s failed: %s", attempt, error
                )
                await asyncio.sleep(min(30, 2 ** min(attempt, 5)))

    async def _restore_native_subscriptions(self) -> None:
        async with self._subscription_lock:
            subscriptions = list(self._subscriptions.items())
            for key, subscription in subscriptions:
                self._unsubscribe_native(subscription.native_id)
                callback = lambda payload, native_key=key: self._market_callback(
                    native_key, payload
                )

                def subscribe_native(
                    item: NativeSubscription = subscription,
                    handler: Any = callback,
                ) -> int:
                    return integer(
                        self.xtdata.subscribe_quote(
                            item.symbol,
                            period=item.period,
                            count=0,
                            callback=handler,
                        )
                    )

                native_id = await asyncio.to_thread(subscribe_native)
                if native_id <= 0:
                    raise RuntimeError(
                        f"Failed to restore {subscription.symbol}/{subscription.period}."
                    )
                subscription.native_id = native_id


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="StockSharp local gateway for XtQuant/MiniQMT."
    )
    parser.add_argument(
        "--qmt-path",
        required=True,
        help="Absolute MiniQMT userdata_mini directory.",
    )
    parser.add_argument("--account", required=True, help="MiniQMT account identifier.")
    parser.add_argument(
        "--account-type",
        default="STOCK",
        help="XtQuant account type (default: STOCK).",
    )
    parser.add_argument(
        "--token",
        required=True,
        help="Shared secret required from every StockSharp client.",
    )
    parser.add_argument("--host", default="127.0.0.1", help="Gateway bind address.")
    parser.add_argument("--port", type=int, default=58630, help="Gateway TCP port.")
    parser.add_argument(
        "--session-id",
        type=int,
        default=58630,
        help="Unique XtQuantTrader session identifier.",
    )
    parser.add_argument(
        "--quote-port",
        type=int,
        default=0,
        help="Optional explicit MiniQMT quote port.",
    )
    parser.add_argument(
        "--sectors",
        default="沪深A股,沪市A股,深市A股,北证A股,沪深ETF",
        help="Comma-separated XtQuant sectors used for security lookup.",
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
    )
    arguments = parser.parse_args()
    if not 0 < arguments.port <= 65535:
        parser.error("--port must be between 1 and 65535.")
    if arguments.quote_port < 0 or arguments.quote_port > 65535:
        parser.error("--quote-port must be between 0 and 65535.")
    if arguments.session_id <= 0:
        parser.error("--session-id must be positive.")
    if not arguments.token.strip():
        parser.error("--token cannot be empty.")
    return arguments


async def run(arguments: argparse.Namespace) -> None:
    gateway = QmtGateway(arguments)
    stop_event = asyncio.Event()
    loop = asyncio.get_running_loop()
    for signal_name in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(signal_name, stop_event.set)
        except (NotImplementedError, RuntimeError):
            pass

    gateway_task = asyncio.create_task(gateway.start())
    stop_task = asyncio.create_task(stop_event.wait())
    completed, _ = await asyncio.wait(
        {gateway_task, stop_task}, return_when=asyncio.FIRST_COMPLETED
    )
    if gateway_task in completed:
        stop_task.cancel()
        await gateway_task
    else:
        gateway_task.cancel()
        await gateway.stop()
        try:
            await gateway_task
        except asyncio.CancelledError:
            pass


def main() -> int:
    arguments = parse_arguments()
    logging.basicConfig(
        level=getattr(logging, arguments.log_level),
        format="%(asctime)s %(levelname)s %(message)s",
    )
    try:
        asyncio.run(run(arguments))
        return 0
    except KeyboardInterrupt:
        return 0
    except Exception:
        logging.exception("QMT gateway stopped.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
