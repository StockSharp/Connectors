package com.stocksharp.dukascopy;

import com.dukascopy.api.Filter;
import com.dukascopy.api.IAccount;
import com.dukascopy.api.IBar;
import com.dukascopy.api.IContext;
import com.dukascopy.api.IEngine;
import com.dukascopy.api.IHistory;
import com.dukascopy.api.IMessage;
import com.dukascopy.api.IOrder;
import com.dukascopy.api.IStrategy;
import com.dukascopy.api.ITick;
import com.dukascopy.api.Instrument;
import com.dukascopy.api.JFException;
import com.dukascopy.api.OfferSide;
import com.dukascopy.api.Period;
import com.dukascopy.api.system.ClientFactory;
import com.dukascopy.api.system.IClient;
import com.dukascopy.api.system.ISystemListener;
import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.PropertyNamingStrategies;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

public final class BridgeMain {
    private BridgeMain() {
    }

    public static void main(String[] args) throws Exception {
        int port = parsePort(args);
        InetAddress loopback = InetAddress.getLoopbackAddress();
        try (ServerSocket server = new ServerSocket()) {
            server.bind(new InetSocketAddress(loopback, port), 1);
            while (!Thread.currentThread().isInterrupted()) {
                try (Socket socket = server.accept()) {
                    if (!socket.getInetAddress().isLoopbackAddress()) {
                        continue;
                    }
                    new BridgeSession(socket).run();
                } catch (IOException error) {
                    if (server.isClosed()) {
                        break;
                    }
                }
            }
        }
    }

    private static int parsePort(String[] args) {
        for (int i = 0; i + 1 < args.length; i++) {
            if ("--port".equals(args[i])) {
                int port = Integer.parseInt(args[i + 1]);
                if (port < 1 || port > 65535) {
                    throw new IllegalArgumentException("Port must be between 1 and 65535.");
                }
                return port;
            }
        }
        return 27431;
    }

    private static final class BridgeSession implements IStrategy {
        private static final String DEMO_URL = "http://platform.dukascopy.com/demo_3/jforex_3.jnlp";
        private static final String LIVE_URL = "http://platform.dukascopy.com/live_3/jforex_3.jnlp";
        private static final int MAX_HISTORY_ITEMS = 100000;

        private final Socket socket;
        private final ObjectMapper mapper;
        private final BufferedReader reader;
        private final BufferedWriter writer;
        private final Set<Instrument> subscribed = new HashSet<Instrument>();
        private final CountDownLatch connected = new CountDownLatch(1);
        private final CountDownLatch contextReady = new CountDownLatch(1);
        private volatile IClient client;
        private volatile IContext context;
        private volatile IHistory history;
        private volatile IEngine engine;
        private volatile boolean running = true;
		private volatile boolean intentionalDisconnect;

        BridgeSession(Socket socket) throws IOException {
            this.socket = socket;
            this.mapper = new ObjectMapper()
                    .setPropertyNamingStrategy(PropertyNamingStrategies.SNAKE_CASE)
                    .setSerializationInclusion(JsonInclude.Include.NON_NULL)
                    .configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);
            this.reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
            this.writer = new BufferedWriter(new OutputStreamWriter(socket.getOutputStream(), StandardCharsets.UTF_8));
        }

        void run() {
            try {
                String line;
                while (running && (line = reader.readLine()) != null) {
                    if (line.length() == 0) {
                        continue;
                    }
                    Request request = mapper.readValue(line, Request.class);
                    handle(request);
                }
            } catch (Exception error) {
                if (running) {
                    send(Message.error(0, messageOf(error)));
                }
            } finally {
                running = false;
                disconnectClient();
            }
        }

        private void handle(Request request) {
            try {
                Message response;
                if ("connect".equals(request.command)) {
                    response = connect(request);
                } else if ("disconnect".equals(request.command)) {
                    disconnectClient();
                    response = Message.response(request.requestId);
                } else {
                    ensureConnected();
                    response = execute(request);
                }
                send(response);
            } catch (Exception error) {
                send(Message.error(request.requestId, messageOf(error)));
            }
        }

        private Message connect(Request request) throws Exception {
            if (client != null) {
                throw new IllegalStateException("The JForex client is already connected.");
            }
            if (isBlank(request.userName) || isBlank(request.password)) {
                throw new IllegalArgumentException("JForex user name and password are required.");
            }

            client = ClientFactory.getDefaultInstance();
            client.setSystemListener(new ISystemListener() {
                @Override
                public void onStart(long processId) {
                }

                @Override
                public void onStop(long processId) {
                }

                @Override
                public void onConnect() {
                    connected.countDown();
                }

                @Override
                public void onDisconnect() {
					IClient current = client;
					if (running && !intentionalDisconnect) {
                        send(Message.eventError("JForex disconnected from the trading server."));
						if (current != null && current.isReconnectAllowed()) {
							current.reconnect();
						}
                    }
                }
            });

            client.connect(Boolean.TRUE.equals(request.isDemo) ? DEMO_URL : LIVE_URL,
                    request.userName, request.password);
            if (!connected.await(60, TimeUnit.SECONDS) || !client.isConnected()) {
                throw new IOException("Timed out while connecting to the JForex trading server.");
            }
            client.startStrategy(this);
            if (!contextReady.await(30, TimeUnit.SECONDS)) {
                throw new IOException("Timed out while starting the JForex bridge strategy.");
            }
            return Message.response(request.requestId);
        }

        private Message execute(final Request request) throws Exception {
            if ("instruments".equals(request.command)) {
                Message result = Message.response(request.requestId);
                result.instruments = new ArrayList<InstrumentDto>();
                List<Instrument> available = new ArrayList<Instrument>(client.getAvailableInstruments());
                Collections.sort(available);
                for (Instrument instrument : available) {
                    result.instruments.add(InstrumentDto.from(instrument));
                }
                return result;
            }
            if ("subscribe".equals(request.command)) {
                final Set<Instrument> instruments = resolveInstruments(request.symbols);
                strategyTask(new Callable<Void>() {
                    @Override
                    public Void call() throws Exception {
                        subscribed.addAll(instruments);
                        context.setSubscribedInstruments(new HashSet<Instrument>(subscribed), true);
                        return null;
                    }
                });
                return Message.response(request.requestId);
            }
            if ("unsubscribe".equals(request.command)) {
                final Set<Instrument> instruments = resolveInstruments(request.symbols);
                strategyTask(new Callable<Void>() {
                    @Override
                    public Void call() throws Exception {
                        context.unsubscribeInstruments(instruments);
                        subscribed.removeAll(instruments);
                        return null;
                    }
                });
                return Message.response(request.requestId);
            }
            if ("history_ticks".equals(request.command)) {
                return historyTicks(request);
            }
            if ("history_bars".equals(request.command)) {
                return historyBars(request);
            }
            if ("place_order".equals(request.command)) {
                return placeOrder(request);
            }
            if ("replace_order".equals(request.command)) {
                return replaceOrder(request);
            }
            if ("cancel_order".equals(request.command)) {
                return cancelOrder(request);
            }
            if ("orders".equals(request.command)) {
                Message result = Message.response(request.requestId);
                result.orders = strategyTask(new Callable<List<OrderDto>>() {
                    @Override
                    public List<OrderDto> call() throws Exception {
                        List<OrderDto> orders = new ArrayList<OrderDto>();
                        for (IOrder order : engine.getOrders()) {
                            orders.add(OrderDto.from(order, null));
                        }
                        return orders;
                    }
                });
                return result;
            }
            if ("account".equals(request.command)) {
                Message result = Message.response(request.requestId);
                result.account = strategyTask(new Callable<AccountDto>() {
                    @Override
                    public AccountDto call() throws Exception {
                        return AccountDto.from(context.getAccount());
                    }
                });
                return result;
            }
            throw new IllegalArgumentException("Unknown bridge command: " + request.command);
        }

        private Message historyTicks(final Request request) throws Exception {
            validateHistoryRequest(request);
            final Instrument instrument = resolveInstrument(request.symbol);
            final int count = boundedCount(request.count);
            Message result = Message.response(request.requestId);
            result.ticks = strategyTask(new Callable<List<TickDto>>() {
                @Override
                public List<TickDto> call() throws Exception {
                    List<ITick> ticks = history.getTicks(instrument, request.from, request.to);
                    int fromIndex = Math.max(0, ticks.size() - count);
                    List<TickDto> values = new ArrayList<TickDto>(ticks.size() - fromIndex);
                    for (int i = fromIndex; i < ticks.size(); i++) {
                        values.add(TickDto.from(instrument, ticks.get(i)));
                    }
                    return values;
                }
            });
            return result;
        }

        private Message historyBars(final Request request) throws Exception {
            validateHistoryRequest(request);
            final Instrument instrument = resolveInstrument(request.symbol);
            final Period period = resolvePeriod(request.period);
            final int count = boundedCount(request.count);
            Message result = Message.response(request.requestId);
            result.bars = strategyTask(new Callable<List<BarDto>>() {
                @Override
                public List<BarDto> call() throws Exception {
                    long from = history.getBarStart(period, request.from);
                    long to = history.getBarStart(period, request.to);
                    List<IBar> bids = history.getBars(instrument, period, OfferSide.BID,
                            Filter.NO_FILTER, from, to);
                    List<IBar> asks = history.getBars(instrument, period, OfferSide.ASK,
                            Filter.NO_FILTER, from, to);
                    Map<Long, IBar> asksByTime = new HashMap<Long, IBar>();
                    for (IBar ask : asks) {
                        asksByTime.put(ask.getTime(), ask);
                    }
                    int fromIndex = Math.max(0, bids.size() - count);
                    List<BarDto> values = new ArrayList<BarDto>(bids.size() - fromIndex);
                    for (int i = fromIndex; i < bids.size(); i++) {
                        IBar bid = bids.get(i);
                        values.add(BarDto.from(instrument, period, bid, asksByTime.get(bid.getTime())));
                    }
                    return values;
                }
            });
            return result;
        }

        private Message placeOrder(final Request request) throws Exception {
            if (request.amount == null || request.amount <= 0) {
                throw new IllegalArgumentException("JForex amount must be positive.");
            }
            final Instrument instrument = resolveInstrument(request.symbol);
            final IEngine.OrderCommand command = IEngine.OrderCommand.valueOf(request.orderCommand);
            final double price = valueOr(request.price, 0);
            final double slippage = valueOr(request.slippage, -1);
            final double stopLoss = valueOr(request.stopLossPrice, 0);
            final double takeProfit = valueOr(request.takeProfitPrice, 0);
            final long goodTill = request.goodTillTime == null ? 0 : request.goodTillTime;
            final String label = required(request.label, "label");

            IOrder order = strategyTask(new Callable<IOrder>() {
                @Override
                public IOrder call() throws Exception {
                    subscribed.add(instrument);
                    context.setSubscribedInstruments(new HashSet<Instrument>(subscribed), true);
					IOrder order = engine.submitOrder(label, instrument, command, request.amount,
                            price, slippage, stopLoss, takeProfit, goodTill, request.comment);
					order.waitForUpdate(10000);
					return order;
                }
            });
            Message result = Message.response(request.requestId);
            result.order = OrderDto.from(order, null);
            return result;
        }

        private Message replaceOrder(final Request request) throws Exception {
            final String orderId = required(request.orderId, "order_id");
            IOrder order = strategyTask(new Callable<IOrder>() {
                @Override
                public IOrder call() throws Exception {
                    IOrder current = requireOrder(orderId);
                    List<RunnableWithException> changes = new ArrayList<RunnableWithException>();
                    if (request.amount != null) {
                        changes.add(new RunnableWithException() {
                            @Override
                            public void run() throws Exception {
                                current.setRequestedAmount(request.amount);
                            }
                        });
                    }
                    if (request.price != null && request.price > 0) {
                        changes.add(new RunnableWithException() {
                            @Override
                            public void run() throws Exception {
                                current.setOpenPrice(request.price);
                            }
                        });
                    }
                    if (request.stopLossPrice != null) {
                        changes.add(new RunnableWithException() {
                            @Override
                            public void run() throws Exception {
                                current.setStopLossPrice(request.stopLossPrice);
                            }
                        });
                    }
                    if (request.takeProfitPrice != null) {
                        changes.add(new RunnableWithException() {
                            @Override
                            public void run() throws Exception {
                                current.setTakeProfitPrice(request.takeProfitPrice);
                            }
                        });
                    }
                    if (request.goodTillTime != null) {
                        changes.add(new RunnableWithException() {
                            @Override
                            public void run() throws Exception {
                                current.setGoodTillTime(request.goodTillTime);
                            }
                        });
                    }
                    for (int i = 0; i < changes.size(); i++) {
                        changes.get(i).run();
                        if (i + 1 < changes.size()) {
                            current.waitForUpdate(2000);
                        }
                    }
                    return current;
                }
            });
            Message result = Message.response(request.requestId);
            result.order = OrderDto.from(order, null);
            return result;
        }

        private Message cancelOrder(final Request request) throws Exception {
            final String orderId = required(request.orderId, "order_id");
            strategyTask(new Callable<Void>() {
                @Override
                public Void call() throws Exception {
                    requireOrder(orderId).close();
                    return null;
                }
            });
            return Message.response(request.requestId);
        }

        private IOrder requireOrder(String orderId) throws JFException {
            IOrder order = engine.getOrderById(orderId);
            if (order == null) {
                throw new IllegalArgumentException("JForex order was not found: " + orderId);
            }
            return order;
        }

        private <T> T strategyTask(Callable<T> callable) throws Exception {
            Future<T> future = context.executeTask(callable);
            return future.get(120, TimeUnit.SECONDS);
        }

        private void ensureConnected() {
            if (client == null || !client.isConnected() || context == null) {
                throw new IllegalStateException("The JForex client is not connected.");
            }
        }

        private void validateHistoryRequest(Request request) {
            if (request.from == null || request.to == null || request.from > request.to) {
                throw new IllegalArgumentException("A valid UTC history interval is required.");
            }
        }

        private int boundedCount(Integer count) {
            return Math.max(1, Math.min(count == null ? 10000 : count, MAX_HISTORY_ITEMS));
        }

        private Set<Instrument> resolveInstruments(List<String> symbols) {
            if (symbols == null || symbols.isEmpty()) {
                return Collections.emptySet();
            }
            Set<Instrument> result = new HashSet<Instrument>();
            for (String symbol : symbols) {
                result.add(resolveInstrument(symbol));
            }
            return result;
        }

        private Instrument resolveInstrument(String symbol) {
            Instrument instrument = Instrument.fromString(required(symbol, "symbol").replace('_', '/').toUpperCase());
            if (instrument == null) {
                throw new IllegalArgumentException("Unknown JForex instrument: " + symbol);
            }
            return instrument;
        }

        private Period resolvePeriod(String value) {
            if ("TEN_SECS".equals(value)) return Period.TEN_SECS;
            if ("ONE_MIN".equals(value)) return Period.ONE_MIN;
            if ("FIVE_MINS".equals(value)) return Period.FIVE_MINS;
            if ("TEN_MINS".equals(value)) return Period.TEN_MINS;
            if ("FIFTEEN_MINS".equals(value)) return Period.FIFTEEN_MINS;
            if ("THIRTY_MINS".equals(value)) return Period.THIRTY_MINS;
            if ("ONE_HOUR".equals(value)) return Period.ONE_HOUR;
            if ("FOUR_HOURS".equals(value)) return Period.FOUR_HOURS;
            if ("DAILY".equals(value)) return Period.DAILY;
            if ("WEEKLY".equals(value)) return Period.WEEKLY;
            if ("MONTHLY".equals(value)) return Period.MONTHLY;
            throw new IllegalArgumentException("Unsupported JForex period: " + value);
        }

        private void disconnectClient() {
            IClient current = client;
            client = null;
            if (current != null) {
                try {
                    if (current.isConnected()) {
						intentionalDisconnect = true;
                        current.disconnect();
                    }
                } catch (Exception ignored) {
                }
            }
            context = null;
            history = null;
            engine = null;
            subscribed.clear();
        }

        private synchronized void send(Message message) {
            if (!running) {
                return;
            }
            try {
                writer.write(mapper.writeValueAsString(message));
                writer.newLine();
                writer.flush();
            } catch (IOException error) {
                running = false;
                try {
                    socket.close();
                } catch (IOException ignored) {
                }
            }
        }

        @Override
        public void onStart(IContext value) {
            context = value;
            history = value.getHistory();
            engine = value.getEngine();
            contextReady.countDown();
        }

        @Override
        public void onStop() {
        }

        @Override
        public void onTick(Instrument instrument, ITick tick) {
            Message event = Message.event("tick");
            event.tick = TickDto.from(instrument, tick);
            send(event);
        }

        @Override
        public void onBar(Instrument instrument, Period period, IBar askBar, IBar bidBar) {
            Message event = Message.event("bar");
            event.bar = BarDto.from(instrument, period, bidBar, askBar);
            send(event);
        }

        @Override
        public void onMessage(IMessage message) {
            if (message.getOrder() == null) {
                return;
            }
            Message event = Message.event("order");
            event.order = OrderDto.from(message.getOrder(), message.getContent());
            send(event);
        }

        @Override
        public void onAccount(IAccount account) {
            Message event = Message.event("account");
            event.account = AccountDto.from(account);
            send(event);
        }
    }

    private interface RunnableWithException {
        void run() throws Exception;
    }

    public static final class Request {
        public long requestId;
        public String command;
        public String userName;
        public String password;
        public Boolean isDemo;
        public List<String> symbols;
        public String symbol;
        public String period;
        public Long from;
        public Long to;
        public Integer count;
        public String orderId;
        public String label;
        public String orderCommand;
        public Double amount;
        public Double price;
        public Double slippage;
        public Double stopLossPrice;
        public Double takeProfitPrice;
        public Long goodTillTime;
        public String comment;
    }

    public static final class Message {
        public String kind;
        public long requestId;
        public String error;
        public List<InstrumentDto> instruments;
        public List<TickDto> ticks;
        public List<BarDto> bars;
        public List<OrderDto> orders;
        public TickDto tick;
        public BarDto bar;
        public OrderDto order;
        public AccountDto account;

        static Message response(long requestId) {
            Message value = new Message();
            value.kind = "response";
            value.requestId = requestId;
            return value;
        }

        static Message error(long requestId, String error) {
            Message value = response(requestId);
            value.error = error;
            return value;
        }

        static Message event(String kind) {
            Message value = new Message();
            value.kind = kind;
            return value;
        }

        static Message eventError(String error) {
            Message value = event("error");
            value.error = error;
            return value;
        }
    }

    public static final class InstrumentDto {
        public String symbol;
        public String name;
        public String type;
        public String primaryCurrency;
        public String secondaryCurrency;
        public double pipValue;
        public int pipScale;
        public int tickScale;
        public double minTradeAmount;

        static InstrumentDto from(Instrument instrument) {
            InstrumentDto value = new InstrumentDto();
            value.symbol = instrument.toString();
            value.name = instrument.getName();
            value.type = instrument.getType() == null ? null : instrument.getType().toString();
            value.primaryCurrency = instrument.getPrimaryJFCurrency() == null ? null : instrument.getPrimaryJFCurrency().toString();
            value.secondaryCurrency = instrument.getSecondaryJFCurrency() == null ? null : instrument.getSecondaryJFCurrency().toString();
            value.pipValue = instrument.getPipValue();
            value.pipScale = instrument.getPipScale();
            value.tickScale = instrument.getTickScale();
            value.minTradeAmount = instrument.getMinTradeAmount();
            return value;
        }
    }

    public static final class TickDto {
        public String symbol;
        public long time;
        public double ask;
        public double bid;
        public double askVolume;
        public double bidVolume;
        public double[] askPrices;
        public double[] askVolumes;
        public double[] bidPrices;
        public double[] bidVolumes;
        public double totalAskVolume;
        public double totalBidVolume;

        static TickDto from(Instrument instrument, ITick tick) {
            TickDto value = new TickDto();
            value.symbol = instrument.toString();
            value.time = tick.getTime();
            value.ask = tick.getAsk();
            value.bid = tick.getBid();
            value.askVolume = tick.getAskVolume();
            value.bidVolume = tick.getBidVolume();
            value.askPrices = tick.getAsks();
            value.askVolumes = tick.getAskVolumes();
            value.bidPrices = tick.getBids();
            value.bidVolumes = tick.getBidVolumes();
            value.totalAskVolume = tick.getTotalAskVolume();
            value.totalBidVolume = tick.getTotalBidVolume();
            return value;
        }
    }

    public static final class BarDto {
        public String symbol;
        public String period;
        public long time;
        public double bidOpen;
        public double bidHigh;
        public double bidLow;
        public double bidClose;
        public double bidVolume;
        public double askOpen;
        public double askHigh;
        public double askLow;
        public double askClose;
        public double askVolume;

        static BarDto from(Instrument instrument, Period period, IBar bid, IBar ask) {
            BarDto value = new BarDto();
            value.symbol = instrument.toString();
            value.period = periodName(period);
            IBar timeBar = bid == null ? ask : bid;
            value.time = timeBar == null ? 0 : timeBar.getTime();
            if (bid != null) {
                value.bidOpen = bid.getOpen();
                value.bidHigh = bid.getHigh();
                value.bidLow = bid.getLow();
                value.bidClose = bid.getClose();
                value.bidVolume = bid.getVolume();
            }
            if (ask != null) {
                value.askOpen = ask.getOpen();
                value.askHigh = ask.getHigh();
                value.askLow = ask.getLow();
                value.askClose = ask.getClose();
                value.askVolume = ask.getVolume();
            }
            return value;
        }
    }

    public static final class OrderDto {
        public String id;
        public String label;
        public String symbol;
        public String command;
        public String state;
        public double amount;
        public double requestedAmount;
        public double filledAmount;
        public double openPrice;
        public double closePrice;
        public long creationTime;
        public long fillTime;
        public long closeTime;
        public double stopLossPrice;
        public double takeProfitPrice;
        public long goodTillTime;
        public double profitLoss;
        public String comment;
        public String message;

        static OrderDto from(IOrder order, String message) {
            OrderDto value = new OrderDto();
            value.id = order.getId();
            value.label = order.getLabel();
            value.symbol = order.getInstrument().toString();
            value.command = order.getOrderCommand().toString();
            value.state = order.getState().toString();
            value.amount = order.getOriginalAmount();
            value.requestedAmount = order.getRequestedAmount();
            value.filledAmount = order.getState() == IOrder.State.FILLED || order.getState() == IOrder.State.CLOSED
                    ? order.getAmount() : 0;
            value.openPrice = order.getOpenPrice();
            value.closePrice = order.getClosePrice();
            value.creationTime = order.getCreationTime();
            value.fillTime = order.getFillTime();
            value.closeTime = order.getCloseTime();
            value.stopLossPrice = order.getStopLossPrice();
            value.takeProfitPrice = order.getTakeProfitPrice();
            value.goodTillTime = order.getGoodTillTime();
            value.profitLoss = order.getProfitLossInAccountCurrency();
            value.comment = order.getComment();
            value.message = message;
            return value;
        }
    }

    public static final class AccountDto {
        public String accountId;
        public String userName;
        public String currency;
        public double balance;
        public double equity;
        public double usedMargin;
        public double useOfLeverage;
        public double creditLine;

        static AccountDto from(IAccount account) {
            if (account == null) {
                return null;
            }
            AccountDto value = new AccountDto();
            value.accountId = account.getAccountId();
            value.userName = account.getUserName();
            value.currency = account.getAccountCurrency() == null ? null : account.getAccountCurrency().toString();
            value.balance = account.getBalance();
            value.equity = account.getEquity();
            value.usedMargin = account.getUsedMargin();
            value.useOfLeverage = account.getUseOfLeverage();
            value.creditLine = account.getCreditLine();
            return value;
        }
    }

    private static String periodName(Period period) {
        if (Period.TEN_SECS.equals(period)) return "TEN_SECS";
        if (Period.ONE_MIN.equals(period)) return "ONE_MIN";
        if (Period.FIVE_MINS.equals(period)) return "FIVE_MINS";
        if (Period.TEN_MINS.equals(period)) return "TEN_MINS";
        if (Period.FIFTEEN_MINS.equals(period)) return "FIFTEEN_MINS";
        if (Period.THIRTY_MINS.equals(period)) return "THIRTY_MINS";
        if (Period.ONE_HOUR.equals(period)) return "ONE_HOUR";
        if (Period.FOUR_HOURS.equals(period)) return "FOUR_HOURS";
        if (Period.DAILY.equals(period)) return "DAILY";
        if (Period.WEEKLY.equals(period)) return "WEEKLY";
        if (Period.MONTHLY.equals(period)) return "MONTHLY";
        return period.toString();
    }

    private static String required(String value, String name) {
        if (isBlank(value)) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return value;
    }

    private static boolean isBlank(String value) {
        return value == null || value.trim().length() == 0;
    }

    private static double valueOr(Double value, double fallback) {
        return value == null ? fallback : value;
    }

    private static String messageOf(Throwable error) {
        Throwable current = error;
        while (current.getCause() != null) {
            current = current.getCause();
        }
        String message = current.getMessage();
        return current.getClass().getSimpleName() + (isBlank(message) ? "" : ": " + message);
    }
}
