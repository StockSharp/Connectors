# Аудит актуальности протоколов коннекторов

Дата проверки: 2026-07-26.

## Область проверки

Проверены все 275 connector-проектов (`*.csproj`, кроме тестовых проектов) в репозитории `Connectors`.

Проверка включала:

- статический просмотр используемых REST, WebSocket, Socket.IO, FIX, protobuf, native/desktop SDK и on-chain протоколов;
- поиск зафиксированных в коде версий API, URL, старых транспортов, незашифрованных соединений и внутренних/недокументированных endpoint;
- сверку явно рискованных интеграций с актуальными публичными страницами поставщиков;
- DNS-проверку подозрительных endpoint и несколько неавторизованных запросов к публичным API.

Ограничения:

- учетные данные поставщиков не использовались;
- authenticated UAT/sandbox/live smoke-тесты не выполнялись;
- проекты не компилировались и тесты не запускались;
- статус `OK` означает, что при статической проверке не найдено публичного подтверждения устаревания. Это не заменяет регулярный authenticated smoke-тест;
- для закрытых SDK, entitlement API и on-chain контрактов публичной проверки недостаточно: их нужно сверять с пакетами/схемами поставщика и проверять в UAT.

В этом документе нет изменений кода. Вынесение адресов хостов в настройки относится к отдельной задаче и здесь не выполнялось.

## Статусы

| Статус | Значение |
|---|---|
| `P0` | Поставщик/торговая площадка закрыты. Коннектор следует вывести из эксплуатации или архивировать. |
| `P1` | Есть подтвержденная несовместимость, мертвый endpoint либо используется уже отключенный API. Нужна замена протокола до дальнейшего развития коннектора. |
| `P2` | API еще может частично работать, но используется устаревающий, небезопасный или недокументированный контракт. Нужна плановая миграция. |
| `P3` | Публично подтвердить совместимость нельзя. Нужны текущий vendor SDK/schema и authenticated UAT. |
| `OK` | Используемое поколение API выглядит актуальным; специальных изменений по результатам этого аудита не требуется. |

Итог классификации: `P0` — 3, `P1` — 9, `P2` — 17, `P3` — 109, `OK` — 137. Всего — 275.

## Критические результаты

### `P0`: вывести из эксплуатации

#### Bittrex

В коде используются старые `https://bittrex.com/api/...`, `https://socket.bittrex.com/signalr` и legacy `Microsoft.AspNet.SignalR.Client` (`Bittrex/Native/HttpClient.cs`, `Bittrex/Native/PusherClient.cs`, `Bittrex/Bittrex.csproj`). Bittrex Global официально объявила сворачивание операций, а обе компании находятся в ликвидации. Переносить этот код на другой API бессмысленно: коннектор следует архивировать или удалить из поставки.

Источник: [официальное уведомление Bittrex Global](https://bittrexglobal.com/).

#### Btce

Коннектор рассчитан на BTC-e/WEX (`Btce/BtceMessageAdapter_Settings.cs`, `Btce/Native/Protocol.cs`). `wex.nz` не разрешается в DNS, а действующего преемника API нет. Коннектор следует архивировать.

#### FTX

В коде остались `https://ftx.com/...` и `wss://ftx.com/ws` (`FTX/Native/FtxRestClient.cs`, `FTX/Native/FtxWebSocketClient.cs`). Действующей торговой площадки и торгового API больше нет; официальный сайт обслуживает требования кредиторов. Коннектор следует архивировать.

Источник: [официальная справка FTX, описывающая биржу как ранее действовавшую](https://support.ftx.com/hc/en-us/articles/19464725450260-Derivative-Positions).

### `P1`: протокол необходимо заменить

#### Poloniex

Текущая реализация вызывает legacy HTTP `https://poloniex.com/public`/`tradingApi` и `wss://api2.poloniex.com` (`Poloniex/Native/HttpClient.cs`, `Poloniex/Native/PusherClient.cs`). Старые private HTTP и private WebSocket были отключены 2023-02-28, а `api2.poloniex.com` больше не разрешается.

Нужно полностью перейти на:

- REST `https://api.poloniex.com/`;
- public/private WebSocket `wss://ws.poloniex.com/ws/public` и `wss://ws.poloniex.com/ws/private`;
- текущую HMAC-SHA256 подпись, новые имена символов и новые DTO.

Источники: [текущий Spot API](https://api-docs.poloniex.com/spot/), [текущий HTTP API](https://api-docs.poloniex.com/spot/api/), [текущий WebSocket API](https://api-docs.poloniex.com/spot/websocket/).

#### HitBTC

Коннектор использует REST `/api/2` и единый WebSocket `/api/2/ws` (`HitBtc/Native/HttpClient.cs`, `HitBtc/Native/PusherClient.cs`). HitBTC прямо помечает API v2 как deprecated, рекомендует v3, а единый streaming endpoint отдельно объявлен deprecated.

Нужна полная миграция REST/DTO/signing на API v3 и разделение потоков market data, trading и account по текущей спецификации.

Источники: [официальная страница API v2 с уведомлением об устаревании](https://api.hitbtc.com/v2), [API v3](https://api.hitbtc.com/).

#### Kraken

Spot-часть использует REST `/0` и WebSocket v1. Это пока поддерживаемый контракт: Kraken обещает сохранять v1, хотя новые возможности появляются только в v2.

Futures-часть реализована неверно: `Kraken/Native/Futures/FuturesHttpClient.cs` обращается к spot-хосту `https://api.kraken.com`, а `Kraken/Native/Futures/FuturesPusherClient.cs` — к spot WebSocket `wss://ws.kraken.com` и использует spot-схемы. Официальный futures API использует отдельные derivatives REST и futures WebSocket.

Нужно:

- переписать Futures на `https://futures.kraken.com/derivatives/api/v3` и `wss://futures.kraken.com/ws/v1` с отдельными futures DTO/auth;
- Spot можно оставить рабочим, но планово перевести WebSocket v1 на v2.

Источники: [сравнение Spot WebSocket v1/v2](https://docs.kraken.com/exchange/guides/websockets/introduction), [Futures REST](https://docs.kraken.com/api/docs/futures-api/trading/get-history), [Futures WebSocket](https://docs.kraken.com/api/docs/futures-api/websocket/book/).

#### FXCM

Коннектор использует `api.fxcm.com`/`api-demo.fxcm.com` и Socket.IO `EIO=3` (`Fxcm/Native/FxcmRestClient.cs`, `Fxcm/Native/FxcmSocketClient.cs`). Оба хоста не разрешались в DNS на дату проверки. Старая спецификация еще описывает `EIO=3`, поэтому сам номер Engine.IO не является доказательством ошибки, но актуальная продуктовая страница FXCM перечисляет FIX, Java и ForexConnect, а REST API не предлагает.

Нужно получить от FXCM письменное подтверждение нового REST endpoint. Если REST снят с поддержки, заменить реализацию на ForexConnect C#, Java/FIX bridge либо закрыть коннектор.

Источники: [текущая страница API FXCM](https://www.fxcm.com/eu/algorithmic-trading/api-trading/), [старая REST/Socket.IO спецификация](https://fxcm-api.readthedocs.io/en/latest/_downloads/3274e66603a66e9c35309035e7930902/Socket%20REST%20API%20Specs.pdf).

#### OpenMarkets

REST/identity endpoint выглядят действующими, но используемые streaming-хосты `md-streams-api.openmarkets.com.au` и `test-md-streams-api.openmarkets.com.au` не разрешяются (`OpenMarkets/Native/OpenMarketsClient.cs`). Сам `HubConnectionBuilder` современный; проблема именно в старом адресе/контракте stream negotiation.

Нужно запросить у OpenMarkets актуальные production/UAT streaming endpoint и схемы, затем обновить market-data и OMS stream negotiation и проверить их с entitlement-аккаунтом.

Источники: [OpenMarkets Developers](https://openmarkets.com.au/developers/), [статус сервисов OpenMarkets](https://status.openmarkets.com.au/).

#### MoexLchi

Коннектор парсит HTML с `investor.moex.com`, включая незашифрованный HTTP (`MoexLchi/CompetitionYear.cs`). Домен перенаправляет на другой сервис, а HTML-страница не является стабильным API.

Нужно либо найти официальный текущий источник результатов ЛЧИ и реализовать его типизированный контракт, либо вывести этот специализированный коннектор из эксплуатации.

#### Bitalong, PrizmBit и ZB

- `Bitalong` обращается к `https://www.<domain>/api/`; публичный API не отвечает стабильно, актуальной официальной документации не найдено.
- `PrizmBit` использует `api.prizmbit.com` и `wss.prizmbit.com`; оба имени не разрешяются.
- `ZB` использует `api.zb.cn`, `trade.zb.cn`, в том числе незашифрованный HTTP; имена не разрешяются.

До получения от владельца площадки действующих endpoint и спецификации эти коннекторы нужно пометить unsupported/quarantined. Если подтверждения нет — архивировать.

### `P2`: обязательная плановая миграция или security update

#### Bithumb

Используется старый `wss://pubwss.bithumb.com/pub/ws`. Актуальная спецификация публикует `wss://ws-api.bithumb.com/websocket/v1` для public и `/websocket/v2/private` для private. Нужно перенести WebSocket и сверить private REST с текущим Open API.

Источник: [текущая WebSocket-спецификация Bithumb](https://apidocs.bithumb.com/reference/%EA%B8%B0%EB%B3%B8-%EC%A0%95%EB%B3%B4).

#### Interactive Brokers

Реализован собственный wire codec, а верхняя известная версия ограничена `MinServerVerRfqFields` (`InteractiveBrokers/ServerVersions.cs`, `InteractiveBrokers/InteractiveBrokersMessageAdapter_Settings.cs`). Текущая TWS API уже содержит более новые поля и protobuf reference. Обратная совместимость может сохранять базовую работу, но новые ответы/поля останутся неподдержанными.

Нужно сделать diff с текущим официальным C# API: server versions, request/response ids, order/contract fields, decimal semantics и protobuf migration. После этого прогнать conformance-тест через актуальные TWS и IB Gateway.

Источник: [актуальная документация TWS API](https://ibkrcampus.com/campus/ibkr-api-page/twsapi-doc/).

#### KotakNeo

Код использует старый login/validate flow `tradeApiLogin`. Официальный SDK уже публикует API/package 2.x и TOTP/MPIN flow. Нужно сравнить все login, order, portfolio и market-data endpoint с v2, затем мигрировать DTO и подпись.

Источник: [официальный Kotak Neo API v2](https://github.com/Kotak-Neo/Kotak-neo-api-v2).

#### NinjaTrader

Проект `NinjaTrader` фактически обращается к `*.tradovateapi.com/v1`, то есть повторяет Tradovate backend. Компании связаны, поэтому это может быть намеренно, однако текущий NinjaTrader отдельно предлагает REST/Swagger и high-performance WebSocket API.

Нужно зафиксировать назначение коннектора:

- если это alias для Tradovate brokerage — оставить, переименовать/описать зависимость и проверить entitlement;
- если это интеграция с новым NinjaTrader Trader API — заменить REST/WS и DTO на его текущую спецификацию.

Источник: [NinjaTrader Trader APIs](https://developer.ninjatrader.com/products/api).

#### Остальные `P2`

- `FivePaisa`: смешаны старый WCF `VendorsAPI/Service1.svc` и V2/V3/V4/OpenFeed. Нужен method-by-method diff текущих login/order/feed контрактов.
- `Huobi`: endpoint работают, но используются legacy brand/domain и отдельный фиксированный signing host. Нужно сверить с текущим HTX API и исключить расхождение host в URL и подписи.
- `Kaiko`: используется `gateway-v0-grpc.kaiko.ovh`. Нужно получить текущий gRPC endpoint/schema у Kaiko и убрать зависимость от `v0`.
- `KoreaInvestment`: production/simulation WebSocket идут через незашифрованный `ws://`. Нужно подтвердить официальный `wss://` и перейти на TLS.
- `OkexHistory`: часть загрузки использует внутренний web endpoint `/priapi/...`, не входящий в публичный OKX API. Нужно заменить на документированный history/archive endpoint.
- `PintuPro`: в коде зафиксированы только UAT endpoint. Нужно получить и проверить production endpoint.
- `PolygonIO`: сервис переименован в Massive; проверить официальный срок поддержки `polygon.io`/`socket.polygon.io` и подготовить переключение домена без смены DTO, если контракт сохранен.
- `Rss`: пример MarketWatch использует HTTP. Перевести на HTTPS и проверить, что feed еще существует.
- `Saxo`: сверить текущий OAuth flow; `logonvalidation` нельзя считать стабильным без проверки действующей документации и sandbox.
- `THORChain`: используется сторонний gateway Liquify. Нужен официальный Nine Realms/Midgard endpoint либо явно поддерживаемый configurable gateway с проверкой схемы.
- `Tinkoff`: сверить generated protobuf и endpoint с текущим T-Bank Invest API после ребрендинга.
- `Usmart`: UAT торговый endpoint использует HTTP. Получить HTTPS endpoint и запретить незашифрованную авторизацию.
- `WooX`: смешаны разные поколения public/private API. Сделать diff с текущей спецификацией и унифицировать REST/WS generation.

## Важные случаи, которые не следует ошибочно считать устаревшими

- `Kraken` Spot WebSocket v1 пока официально поддерживается; переход на v2 рекомендуется, но v1 не является отключенным.
- `XOpenHub` xAPI 2.5 и `wss://ws.xapi.pro/...` присутствуют в текущей официальной спецификации. Номер 2.5 сам по себе не означает устаревание.
- `Upstox` уже использует Market Data Feed v3/protobuf и v3 для критичных order/history методов. Оставшиеся v2 методы нужно проверять пооперационно, но массовая миграция коннектора не требуется.
- `CoinCap` уже использует REST v3 и bearer token, а не старый v2.
- `Robinhood` endpoint `https://agent.robinhood.com/mcp/trading` относится к новому официальному Agentic Trading MCP. Нужно следить за согласованием `MCP-Protocol-Version`, но заменять API сейчас не требуется.
- `Trading212` `/api/v0/equity` и `BitMEX` `/api/v1` являются названиями действующих продуктовых API; маленький номер версии не является доказательством устаревания.
- `Talos` FIX 4.4 — vendor-required протокол. Его нельзя обновлять на другую FIX-версию без спецификации контрагента.

Источники: [Kraken WebSocket versions](https://docs.kraken.com/exchange/guides/websockets/introduction), [X Open Hub xAPI 2.5](https://xopenhub.pro/api/xapi-protocol-documentation/), [Upstox Market Data Feed v3](https://upstox.com/developer/api-documentation/v3/get-market-data-feed/), [Robinhood Agentic Trading](https://robinhood.com/us/en/support/articles/agentic-trading-overview/).

## Полная матрица коннекторов

Для строк `P3` обязательным результатом следующего этапа должен быть не рефакторинг «на глаз», а зафиксированная версия vendor SDK/proto/spec плюс authenticated UAT. Для on-chain коннекторов дополнительно проверяются chain id, contract addresses, ABI, subgraph/indexer schema и поддерживаемые RPC methods.

| Коннектор | Протокол/поколение в коде | Статус | Что делать |
|---|---|---:|---|
| ActivFinancial | Proprietary native/desktop API | `P3` | Сравнить установленный SDK и wire schema с текущим vendor release; UAT. |
| Aerodrome | Base/EVM RPC и contracts | `P3` | Проверить chain, router/pool addresses, ABI и event decoding. |
| Aevo | REST + WebSocket | `OK` | Публичного устаревания не найдено; оставить регулярный smoke-test. |
| AlgoSeek | File/data-provider integration | `P3` | Сверить текущий формат выгрузок и vendor delivery SDK. |
| AliceBlue | REST v2 + Noren WebSocket | `P3` | Сверить auth/session и WebSocket schema с текущим broker API; UAT. |
| Alor | OAuth + REST/OpenAPI + WebSocket | `OK` | Поколение API выглядит актуальным. |
| AlorHistory | REST history API | `OK` | Специальных изменений протокола не выявлено. |
| Alpaca | Trading/Data REST v2 + WebSocket | `OK` | Актуальное поколение; контролировать changelog data streams. |
| AlphaVantage | REST query API | `OK` | Публичного устаревания не найдено. |
| Amberdata | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Anchorage | REST v2 | `P3` | Подтвердить entitlement, auth и текущую institutional schema в UAT. |
| AngelOne | SmartAPI REST + WebSocket | `P3` | Сверить login/token/feed generation с текущим SmartAPI SDK. |
| ApexOmni | REST + WebSocket | `OK` | Актуальное поколение; нужен обычный authenticated smoke-test. |
| Aster | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Avantis | feed-v3 + on-chain contracts | `P3` | Проверить feed schema, chain/contracts и ABI. |
| Backpack | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Balancer | API v3/GraphQL + EVM | `P3` | Проверить subgraph/API schema и deployed contract addresses. |
| BarChart | REST market-data API | `OK` | Публичного устаревания не найдено. |
| Benzinga | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Bibox | REST v4 | `P3` | Публичный probe был нестабилен; подтвердить текущий API и authenticated UAT у поставщика. |
| Binance | Spot v3/SAPI + Futures REST/WS | `OK` | Используются действующие поколения; мониторить exchange changelog. |
| BingX | REST/WS v1-v3 по продуктам | `OK` | Версии соответствуют разным действующим продуктам; smoke-test. |
| Bitalong | Legacy REST | `P1` | Quarantine; запросить действующую спецификацию или архивировать. |
| Bitbank | REST v1 + Socket.IO EIO4 | `OK` | Публичного подтверждения устаревания не найдено. |
| Bitexbook | REST v2 + WebSocket | `OK` | Endpoint отвечал; оставить smoke-test. |
| Bitfinex | REST v2 + WebSocket v2 | `OK` | Актуальное поколение. |
| BitFlyer | REST + JSON-RPC WebSocket | `OK` | Публичного устаревания не найдено. |
| Bitget | REST/WS v2 | `OK` | Актуальное поколение. |
| BitGo | REST v2 | `OK` | Публичного устаревания не найдено. |
| Bithumb | Legacy REST/старый public WebSocket | `P2` | Перенести WS на `ws-api` v1/v2 и сверить private REST. |
| Bitkub | REST + WebSocket v3 | `OK` | Актуальное поколение. |
| Bitmart | REST/WS v2 | `OK` | Публичного устаревания не найдено. |
| Bitmex | REST/WS API v1 | `OK` | Это действующая продуктовая версия; менять только по changelog. |
| BitpandaFusion | Fusion REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Bitrue | Смешанные REST v1/v2 + WebSocket | `P3` | Сверить каждый spot/futures метод и auth с текущей vendor spec. |
| Bitso | REST v3 + WebSocket | `OK` | Актуальное поколение. |
| BitStamp | REST v2 + WebSocket | `OK` | Публичного устаревания не найдено. |
| Bittrex | Legacy REST + SignalR | `P0` | Архивировать/вывести из поставки. |
| Bitunix | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| Bitvavo | REST/WS v2 | `OK` | Актуальное поколение. |
| BloFin | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| Bloomberg | Proprietary Terminal/Desktop SDK | `P3` | Сверить установленный Bloomberg SDK и entitlement; UAT. |
| Bluefin | REST + WebSocket | `OK` | Публичного устаревания не найдено; проверить product changelog. |
| Bmll | Licensed REST API + version header | `P3` | Сверить `x-bmll-version` и schema с текущим BMLL SDK. |
| Breeze | ICICI Breeze REST v1/v2 + Socket.IO EIO4 | `P3` | Сверить auth, order/feed endpoints и master-file format с текущим SDK. |
| Btce | BTC-e/WEX API | `P0` | Архивировать/вывести из поставки. |
| BTCMarkets | REST + WebSocket v2 | `OK` | Актуальное поколение. |
| BTSE | Spot/Futures REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Bullish | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| ByBit | Unified API v5 | `OK` | Актуальное поколение. |
| BYDFi | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| CapitalCom | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| CapitalFutures | Proprietary SDK | `P3` | Сверить native SDK/protocol с текущим vendor package; UAT. |
| CboeDataShop | REST v1 | `OK` | Номер v1 является текущим продуктовым namespace; smoke-test. |
| Cetus | Sui RPC/on-chain contracts | `P3` | Проверить package ids, ABI/events и RPC compatibility. |
| Cex | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| ChainlinkDataStreams | Data Streams REST/report schema | `P3` | Сверить feed ids, report schema и auth с текущей документацией. |
| CoinApi | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| Coinbase | Advanced Trade REST + WebSocket | `OK` | Используется текущее поколение, не legacy Coinbase Pro. |
| CoinCap | REST v3 + WebSocket | `OK` | REST уже актуализирован; проверить WebSocket smoke-test. |
| Coincheck | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| CoinDCX | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| CoinEx | REST/WS v2 | `OK` | Актуальное поколение. |
| CoinGecko | REST v3 + WebSocket | `OK` | Публичного устаревания не найдено. |
| Coinhako | Public/private API v1 | `P3` | Подтвердить, что v1 остается текущим контрактом для выданного entitlement. |
| Coinigy | REST/WS v2 | `OK` | Публичного устаревания не найдено. |
| CoinJar | Exchange REST + Phoenix WebSocket | `OK` | Публичного устаревания не найдено. |
| CoinMarketCap | REST v1 | `OK` | Это текущий namespace продукта; менять только по changelog. |
| CoinMetrics | REST v4 | `OK` | Актуальное поколение. |
| Coinone | Public v2/private v2.1 + WebSocket | `OK` | Поколение выглядит актуальным. |
| CoinsPh | REST/WS | `OK` | Публичного устаревания не найдено. |
| CoinW | Spot/Futures v1 + отдельные WS-домены | `P3` | Сверить домены, signing и схемы обоих продуктов с текущей spec. |
| Copper | Institutional REST/streaming | `P3` | Нужны текущая entitlement spec и authenticated UAT. |
| CowProtocol | REST + EVM settlement contracts | `P3` | Проверить API schema, chain/contracts и order signing domain. |
| CQG | Proprietary SDK/API | `P3` | Сверить vendor assemblies/proto и gateway compatibility. |
| CryptoCom | Exchange REST/WS v1 | `OK` | v1 является действующим namespace; smoke-test. |
| CryptoQuant | REST v1 | `OK` | Публичного устаревания не найдено. |
| CSV | Локальный файловый формат | `OK` | Удаленного протокола нет; проверять только совместимость формата StockSharp. |
| CTP | Native CTP API | `P3` | Сверить headers/binaries и broker front protocol version. |
| cTrader | Open API protobuf/TCP-TLS | `P3` | Сверить `.proto`, message ids и application entitlement с текущим Open API. |
| Curve | EVM RPC/contracts/indexer | `P3` | Проверить addresses, ABI, events и indexer schema. |
| Daishin | Local COM/native API | `P3` | Сверить с текущей версией Cybos Plus/desktop API. |
| Databento | Historical/live API | `OK` | Используемый namespace выглядит актуальным; smoke-test. |
| Deepcoin | REST + public/private WS v1/v2 | `OK` | Версии относятся к разным потокам; публичного устаревания не найдено. |
| Deribit | JSON-RPC v2 over HTTP/WS | `OK` | Актуальное поколение. |
| Deriv | WebSocket API | `OK` | Публичного устаревания не найдено. |
| Dhan | REST/WS v2 | `OK` | Актуальное поколение. |
| Digifinex | REST v3 + WebSocket v1 | `P3` | Проверить, что WS v1 остается текущим transport для REST v3; UAT. |
| DowJones | Licensed data API | `P3` | Сверить vendor schema/auth и entitlement. |
| Drift | Solana/on-chain + external indexer/feed | `P3` | Проверить program ids, IDL, RPC и источник off-chain данных. |
| DukasCopyLive | Proprietary live/local protocol | `P3` | Сверить протокол с текущим JForex/Dukascopy SDK. |
| DxFeed | dxLink/WebSocket | `OK` | Протокол выглядит актуальным; проверить entitlement UAT. |
| DXtrade | Tenant-specific REST/WebSocket | `P3` | Сверить схему с конкретным DXtrade tenant и его release. |
| DydxChain | v4 indexer + Cosmos/on-chain | `P3` | Поколение v4 актуально; проверить node/indexer schema и chain upgrade. |
| edgeX | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| EodHistoricalData | REST API | `OK` | Публичного устаревания не найдено. |
| Etoro | Public REST API | `OK` | Используется текущий публичный продукт; smoke-test. |
| ETrade | OAuth + REST | `P3` | Сверить OAuth flow, sandbox и account/order schemas с текущим E*TRADE API. |
| Exegy | Proprietary SDK/feed | `P3` | Сверить vendor binaries и feed dictionary. |
| Exmo | REST/WS v1.x | `OK` | Публичного подтверждения устаревания не найдено. |
| Extended | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| FactSet | Licensed REST/streaming | `P3` | Сверить API catalog, auth и entitlement с текущим FactSet SDK. |
| FalconX | Institutional REST/streaming | `P3` | Нужны текущая vendor spec и authenticated UAT. |
| FinancialModelingPrep | Stable REST API | `OK` | Используется `stable` namespace; мониторить changelog. |
| Finnhub | REST/WS v1 | `OK` | v1 остается продуктовым namespace; smoke-test. |
| Fireblocks | REST v1 | `P3` | Сверить signing, vault/account model и API version с entitlement. |
| FivePaisa | WCF + V2/V3/V4 + OpenFeed | `P2` | Сделать method-by-method migration на текущие login/order/feed contracts. |
| Flattrade | Noren REST/WebSocket | `P3` | Сверить broker-specific auth/session и current Noren schema. |
| FluidDex | On-chain DEX protocol | `P3` | Проверить chain, package/contracts, ABI и indexer. |
| Foxbit | REST/WS v3 | `OK` | Актуальное поколение. |
| FTX | Legacy FTX REST/WS | `P0` | Архивировать/вывести из поставки. |
| FubonNeo | Proprietary broker SDK | `P3` | Сверить установленный SDK, native dependencies и market-data schema. |
| Fugle | REST/WS v1 | `P3` | Подтвердить текущую broker API version и auth через UAT. |
| Fxcm | Legacy REST + Socket.IO EIO3 | `P1` | Получить новый endpoint или заменить на ForexConnect/FIX. |
| FXOpen | TickTrader REST/WebSocket | `OK` | Используется актуальное семейство API; оставить sandbox/live smoke-test. |
| Fyers | REST/WS broker API | `P3` | Сверить v3 auth, order и data sockets с текущим SDK. |
| GainsNetwork | REST/indexer + on-chain contracts | `P3` | Проверить backend generation, chain/contracts, ABI и events. |
| GateIO | REST/WS v4 | `OK` | Актуальное поколение. |
| Gemini | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Glassnode | REST v1 | `OK` | v1 является продуктовым namespace; smoke-test. |
| GmoCoin | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| GMTrade | On-chain contracts/indexer | `P3` | Проверить chain, contract addresses, ABI и indexer schema. |
| Gmx | EVM contracts/subgraph | `P3` | Проверить текущую GMX generation, addresses, ABI и indexer. |
| Gopax | REST + WebSocket | `OK` | Публичный endpoint отвечал; оставить authenticated smoke-test. |
| Groww | Broker REST/streaming v1 | `P3` | Сверить auth, instruments, order и stream schema с текущим Groww API. |
| Grvt | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| HashKey | REST/WS v1-v2 | `OK` | Версии относятся к действующим продуктам; smoke-test. |
| HitBtc | Deprecated REST/WS v2 | `P1` | Полностью перейти на API v3 и новые streaming endpoint. |
| Huobi | Legacy Huobi domains + REST/WS | `P2` | Сверить с HTX API и устранить отдельный фиксированный signing host. |
| Hyperliquid | Info/Exchange REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| IG | Versioned REST + Lightstreamer | `P3` | Сверить negotiated API versions, CST/XST auth и Lightstreamer schema. |
| IndependentReserve | REST API | `OK` | Публичного устаревания не найдено. |
| Indodax | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Injective | Cosmos/gRPC/indexer protobuf | `P3` | Сверить generated proto, chain ids, node/indexer endpoints и network upgrade. |
| InteractiveBrokers | Собственный TWS wire codec | `P2` | Сделать diff с текущим C# TWS API и protobuf/server versions; conformance UAT. |
| Intrinio | REST/WS v2 | `OK` | Актуальное поколение. |
| IQFeed | Proprietary desktop TCP protocol | `P3` | Сверить IQFeed client protocol/version и field dictionaries. |
| JpmDataQuery | DataQuery API v2 | `P3` | Подтвердить текущий auth/schema и entitlement в UAT. |
| Jupiter | REST/quote/order API | `OK` | Публичного устаревания не найдено; мониторить Solana transaction schema. |
| KabuStation | Local REST API | `P3` | Проверить совместимость с текущим kabu Station desktop release/API version. |
| Kaiko | gRPC gateway `v0` | `P2` | Получить текущий endpoint/proto у Kaiko и мигрировать с `v0`. |
| Kalshi | REST/WS v2 | `OK` | Актуальное поколение. |
| Kiwoom | Broker REST/WebSocket | `P3` | Сверить app auth, TR ids и payload schema с текущей Kiwoom API. |
| Korbit | REST + WebSocket v2 | `OK` | Поколение выглядит актуальным. |
| KoreaInvestment | REST + незашифрованный WebSocket | `P2` | Получить официальный `wss://`, перейти на TLS и повторить UAT. |
| KotakNeo | Legacy login/order flow | `P2` | Мигрировать auth/DTO/methods на Kotak Neo API 2.x. |
| Kraken | Spot v1 + ошибочная Futures реализация | `P1` | Переписать Futures; Spot WS планово перевести на v2. |
| Kucoin | REST + WebSocket | `OK` | Публичного устаревания не найдено; мониторить KuCoin changelog. |
| Latoken | REST/WS v2 | `P3` | Подтвердить текущую exchange spec и authenticated trading UAT. |
| LBank | REST/WS v2 | `P3` | Публичный REST отвечал; сверить private auth и WS schema с текущей spec. |
| LemonMarkets | REST API v1 | `OK` | v1 остается продуктовым namespace; smoke-test. |
| Lfj | Avalanche/EVM contracts/indexer | `P3` | Проверить текущий бренд/API, chain/contracts, ABI и indexer. |
| Ligther | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Lime | Broker API | `P3` | Сверить auth/order/market-data schema с текущей Lime spec; UAT. |
| LMAX | Licensed WebSocket/API | `P3` | Сверить current endpoint, schema и entitlement с LMAX. |
| Longbridge | OpenAPI REST/WS v2 | `P3` | Поколение выглядит актуальным; проверить SDK parity и authenticated UAT. |
| LsegRealTime | OAuth + Real-Time WebSocket | `P3` | Сверить RDP/LSEG auth scopes, item domains и streaming schema. |
| LsSecurities | Broker REST/WebSocket | `P3` | Сверить TR codes, auth и payload schema с текущей broker spec. |
| Luno | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| ManifestTrade | Solana RPC/WebSocket + stats API | `P3` | Проверить program id/IDL, RPC compatibility и внешний stats backend. |
| Marketstack | REST v2 | `OK` | Актуальное поколение. |
| Marquee | Goldman Sachs Marquee REST | `P3` | Подтвердить OAuth/scopes, API catalog и entitlement. |
| MatchTrader | Tenant-specific REST/WebSocket | `P3` | Сверить endpoints и schema с конкретным broker tenant/release. |
| MercadoBitcoin | REST v4 + streaming | `OK` | Актуальное поколение. |
| MetaApi | REST/streaming v1 | `OK` | Публичного устаревания не найдено. |
| Meteora | Solana programs/indexer | `P3` | Проверить program ids, IDL/accounts и indexer schema. |
| Mexc | Spot/Futures REST + WebSocket | `OK` | Публичного подтверждения устаревания не найдено; smoke-test обоих продуктов. |
| MiraeSharekhan | Broker REST + WebSocket | `P3` | Сверить OAuth/session, service paths и stream schema с текущим API. |
| MoexISS | ISS REST API | `OK` | Публичного устаревания не найдено. |
| MoexLchi | HTML scraping `investor.moex.com` | `P1` | Заменить официальным источником либо вывести коннектор из эксплуатации. |
| Moomoo | Proprietary OpenD/local SDK | `P3` | Сверить OpenD protocol/SDK и минимальную поддерживаемую версию. |
| Morningstar | Licensed data API | `P3` | Сверить auth, feed schema и entitlement с текущим vendor API. |
| MotilalOswal | Broker REST/WebSocket | `P3` | Сверить auth/order/feed contracts с текущим broker release. |
| MtNewswires | Licensed REST v1 | `P3` | Подтвердить текущий namespace, schema и entitlement. |
| Nado | REST/WS v1-v2 | `OK` | Версии относятся к разным операциям; публичного устаревания не найдено. |
| NasdaqCloudDataService | Proprietary SDK/cloud feed | `P3` | Сверить client SDK, schemas и entitlement с текущим Nasdaq release. |
| NasdaqDataLink | REST v3 | `OK` | Актуальное поколение. |
| NDAX | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| NinjaTrader | Tradovate v1 backend | `P2` | Зафиксировать назначение; при необходимости перейти на новый NinjaTrader Trader API. |
| Oanda | REST v20 + pricing stream | `OK` | Актуальное поколение. |
| Okex | OKX REST/WS v5 | `OK` | Актуальное поколение. |
| OkexHistory | Public v5 + undocumented `/priapi` + archives | `P2` | Убрать внутренний web endpoint, оставить документированные API/archive feeds. |
| OneInch | REST v6.1 | `OK` | Актуальное поколение. |
| OpenMarkets | REST + устаревшие streaming hosts | `P1` | Получить текущие prod/UAT stream endpoints и обновить negotiation/schema. |
| OptionMetrics | Licensed data/file API | `P3` | Сверить delivery format, SDK и entitlement с текущим vendor release. |
| Orats | REST API | `OK` | Публичного устаревания не найдено. |
| Orca | Solana programs/indexer | `P3` | Проверить Whirlpool program id, IDL/accounts и RPC behavior. |
| OrderlyNetwork | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| OSL | REST/WS v1-v2 | `P3` | Сверить institutional entitlement, auth и product-specific versions. |
| Osmosis | Cosmos RPC/gRPC/indexer | `P3` | Проверить chain upgrade, proto, node/indexer endpoints и pool model. |
| Ostium | EVM contracts + indexer | `P3` | Проверить chain/contracts, ABI/events и indexer schema. |
| Ourbit | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Pacifica | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| PancakeSwap | EVM contracts/subgraph | `P3` | Проверить router/quoter generation, addresses, ABI и subgraph. |
| Paradex | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Paxos | REST/WS v2 | `OK` | Актуальное поколение. |
| Phemex | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| PhillipPoems | Broker REST gateway | `P3` | Сверить sandbox/prod paths, auth и order/feed schema с POEMS. |
| PintuPro | Только UAT REST/WS endpoints | `P2` | Получить production endpoints и проверить production schema/auth. |
| Pionex | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Poloniex | Отключенный legacy REST/WS | `P1` | Полностью мигрировать на `api.poloniex.com` и `ws.poloniex.com`. |
| PolygonIO | Polygon REST/WS domains | `P2` | Проверить план миграции после ребрендинга Massive и сроки поддержки доменов. |
| Polymarket | CLOB REST/WebSocket | `OK` | Публичного устаревания не найдено. |
| PrizmBit | Неразрешаемые REST/WS hosts | `P1` | Quarantine; получить действующую spec/hosts или архивировать. |
| ProBit | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| Public | Brokerage REST/streaming API | `OK` | Публичного устаревания не найдено; authenticated smoke-test. |
| PumpSwap | Solana on-chain protocol | `P3` | Проверить program id, account layout, instructions/events и RPC. |
| Pyth | Price Service/Hermes + on-chain feeds | `P3` | Сверить feed ids, Hermes schema и chain-specific receiver contracts. |
| QFEX | Authenticated REST/WebSocket | `P3` | Сервис отвечает с auth requirement; нужна текущая vendor spec и UAT. |
| Qmt | Local/native QMT API | `P3` | Сверить local terminal/SDK version и callback schema. |
| QuantFeed | Proprietary market-data protocol | `P3` | Сверить feed dictionary, transport и vendor release. |
| Questrade | OAuth + dynamic REST server | `P3` | Проверить current OAuth scopes, server discovery и account/order UAT. |
| QuickSwap | EVM contracts/subgraph | `P3` | Проверить router generation, chain/contracts, ABI и indexer. |
| Quodd | Licensed market-data API | `P3` | Сверить protocol/schema и entitlement с текущим QUODD release. |
| Rain | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| RakutenRss | Local RSS/desktop API | `P3` | Сверить desktop API version и supported spreadsheet/COM contract. |
| RavenPack | Licensed REST API | `P3` | Сверить API version, taxonomy/schema и entitlement. |
| Raydium | Solana programs/indexer | `P3` | Проверить program ids, IDL/accounts, pool generations и RPC. |
| Reya | REST/WS v2 + on-chain | `P3` | Поколение выглядит актуальным; проверить contracts/schema и UAT. |
| Rithmic | Protobuf gateway, template 3.9 | `P3` | Сверить `.proto`, login template version и gateway hosts с текущим Rithmic kit. |
| Robinhood | Agentic Trading MCP | `OK` | Новый действующий API; следить за negotiated `MCP-Protocol-Version`. |
| Rss | RSS/Atom, один HTTP feed | `P2` | Перевести HTTP feed на HTTPS и проверить доступность каждого preset. |
| Saxo | OpenAPI REST/streaming + OAuth | `P2` | Сверить текущий OAuth/logon flow и API versions в SIM/UAT. |
| Schwab | Trader API REST/OAuth | `OK` | Используется текущее поколение; мониторить auth/order changelog. |
| Shioaji | Local Python/native SDK bridge | `P3` | Сверить SDK package version, native ABI и callbacks. |
| Shoonya | Noren REST/WebSocket | `P3` | Сверить current auth/session, instrument files и Noren schema. |
| SierraChartDtc | DTC protocol | `P3` | Сверить negotiated DTC protocol version и message set с текущим Sierra Chart. |
| SnapTrade | REST v1 | `OK` | Публичного устаревания не найдено. |
| SpGlobal | Licensed data API | `P3` | Сверить auth, schema и entitlement с текущим S&P Global API. |
| StandX | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| StocksTrader | REST + streaming API | `OK` | Публичного устаревания не найдено. |
| SunIo | TRON contracts/indexer | `P3` | Проверить network/contracts, ABI/events и indexer/API schema. |
| Swissquote | REST/streaming v1 | `P3` | Подтвердить institutional API generation и entitlement в UAT. |
| SynFutures | v4 contracts/indexer | `P3` | Поколение v4 выглядит актуальным; проверить addresses, ABI и indexer. |
| Synthetix | EVM contracts/indexer | `P3` | Проверить active protocol generation, deployments, ABI и feeds. |
| Talos | FIX 4.4 over TLS | `P3` | Не менять FIX version без vendor spec; сверить dictionary/extensions и session UAT. |
| Tapbit | Spot v2/derivatives REST + WS | `OK` | Публичного устаревания не найдено. |
| Tardis | REST/WebSocket replay API v1 | `OK` | Публичного устаревания не найдено. |
| TastyTrade | REST/streaming с `Accept-Version` | `OK` | Версия выглядит текущей на дату аудита; автоматизировать changelog check. |
| ThetaData | Local terminal REST v3 + WS v1 | `P3` | Сверить обе версии с установленным Theta Terminal release; local UAT. |
| THORChain | Сторонний Liquify gateway | `P2` | Перейти на официальный/поддерживаемый Midgard/Thornode endpoint и сверить DTO. |
| TigerBrokers | Proprietary OpenAPI SDK | `P3` | Сверить package 1.2.2 с текущим SDK, proto и native dependencies. |
| Tiingo | REST + IEX/crypto WebSocket | `OK` | Публичного устаревания не найдено. |
| Tinkoff | Tinkoff Invest gRPC/protobuf | `P2` | Сверить с текущим T-Bank Invest proto/endpoints и обновить generated DTO. |
| Toobit | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| TradeLocker | REST + WebSocket | `P3` | Поколение выглядит актуальным; проверить broker tenant/auth и UAT. |
| TradeOgre | REST API | `OK` | Публичного устаревания не найдено. |
| TradeStation | REST/streaming v3 | `OK` | Актуальное поколение. |
| TradeZero | Broker REST/streaming v1 | `P3` | Подтвердить current private spec, auth и order/feed UAT. |
| Tradier | REST/streaming v1 | `OK` | v1 остается продуктовым namespace; smoke-test. |
| Trading212 | Equity REST `/api/v0` | `OK` | Это действующий namespace; не обновлять только из-за номера `v0`. |
| TradingTechnologies | TT .NET SDK | `P3` | Сверить assemblies/API version, service environment и entitlement. |
| Tradovate | REST/WS v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| TwelveData | REST/WS v1 | `OK` | v1 остается продуктовым namespace; smoke-test. |
| Uniswap | Trading API v1 + EVM | `P3` | API generation выглядит текущим; проверить chain/contracts и signing UAT. |
| Upbit | REST + WebSocket v1 | `OK` | Публичного подтверждения устаревания не найдено. |
| Upstox | REST v3 + Market Feed v3 protobuf | `OK` | Критичные методы уже актуальны; оставшиеся v2 проверять пооперационно. |
| Usmart | REST/WS, UAT через HTTP | `P2` | Получить HTTPS UAT endpoint и исключить незашифрованную авторизацию. |
| VALR | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| VariationalOmni | REST/WebSocket | `OK` | Публичного устаревания не найдено. |
| VeloData | Versioned REST/streaming | `P3` | Сверить current paths/schema и entitlement с vendor API. |
| Webull | OpenAPI REST/streaming | `OK` | Используется текущий официальный продукт; authenticated smoke-test. |
| Weex | REST + WebSocket v3 | `OK` | Актуальное поколение. |
| WhiteBit | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| WooX | Смешанные public/private REST/WS generations | `P2` | Сверить текущую WOO X spec и унифицировать DTO/auth/streams. |
| XOpenHub | xAPI 2.5 WebSocket | `OK` | Протокол и hosts присутствуют в текущей официальной документации. |
| Xt | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| Xtp | Native XTP API | `P3` | Сверить headers/binaries, protocol version и broker environment. |
| Yobit | Legacy-style REST API | `P3` | Подтвердить private trading/auth и текущую поддержку API через UAT. |
| Yuanta | Proprietary broker SDK | `P3` | Сверить native SDK/protocol и market-data/order callbacks. |
| Zaif | REST + WebSocket | `OK` | Публичного устаревания не найдено. |
| ZB | Мертвые REST/WS hosts, HTTP | `P1` | Quarantine; получить действующую spec/hosts или архивировать. |
| Zerodha | Kite Connect REST/WebSocket | `OK` | Используется текущее поколение; мониторить Kite changelog. |
| ZeroHash | Institutional REST/streaming | `P3` | Сверить API version, auth и entitlement с текущей Zero Hash spec. |
| Zoomex | REST + WebSocket | `OK` | Публичного устаревания не найдено. |

## Рекомендуемый порядок дальнейшей работы

1. Удалить из активной матрицы поддержки `Bittrex`, `Btce`, `FTX`.
2. Решить судьбу коннекторов без действующих endpoint: `Bitalong`, `PrizmBit`, `ZB`.
3. Переписать `Poloniex`, `HitBtc`, Kraken Futures.
4. Получить vendor confirmation для `Fxcm` и новые streaming endpoint для `OpenMarkets`.
5. Убрать HTML/private-web зависимости в `MoexLchi` и `OkexHistory`.
6. Закрыть security gaps в `KoreaInvestment`, `Usmart`, `Rss`.
7. Выполнить плановые `P2`-миграции.
8. Для каждого `P3` зафиксировать vendor SDK/proto/spec version и отдельный UAT checklist.
9. Добавить регулярный authenticated smoke-test и мониторинг deprecation/changelog для всех `OK`-коннекторов.

## Технические наблюдения

- `HttpWebRequest`/`WebRequest.Create` в connector-коде не обнаружены.
- Явной принудительной настройки TLS 1.0/1.1/SSL3 не обнаружено.
- Legacy SignalR найден в `Bittrex`; современный SignalR в `OpenMarkets` сам по себе не является проблемой.
- Socket.IO `EIO=3` найден в `Fxcm`; старая документация FXCM действительно его требует, поэтому причина `P1` — исчезнувшие hosts и отсутствие REST в текущем продуктовом списке, а не одно значение `EIO`.
- FIX 4.4 в `Talos` и другие vendor-specific версии нельзя повышать механически: сначала требуется новый FIX dictionary от контрагента.
- 22 проекта не содержат literal remote URL и зависят от SDK, локального процесса, файла либо vendor gateway discovery: `ActivFinancial`, `AlgoSeek`, `Bloomberg`, `CapitalFutures`, `CSV`, `CTP`, `Daishin`, `DukasCopyLive`, `Exegy`, `FubonNeo`, `Moomoo`, `NasdaqCloudDataService`, `OptionMetrics`, `Qmt`, `QuantFeed`, `RakutenRss`, `SierraChartDtc`, `Talos`, `TigerBrokers`, `TradingTechnologies`, `Xtp`, `Yuanta`. Их нельзя считать проверенными без соответствующей vendor-среды.
