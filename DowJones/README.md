# Dow Jones connector for StockSharp

This connector integrates the current Dow Jones Newswires Real-Time API and the related Content API. Every request and response uses concrete DTOs; the implementation contains no `JObject`, `JArray`, `JToken`, `dynamic`, protocol dictionaries, or untyped protocol payloads.

## Supported functionality

- Historical Dow Jones Newswires headline search by UTC interval.
- Live Newswires subscriptions through cursor-based Real-Time API polling.
- Optional ticker or ISIN filtering through documented Newswires taxonomy queries.
- Optional Unified Query Language filter shared by history and live requests.
- Headline coding, DJN coding, source, language, significance, and organization metadata.
- Full licensed article retrieval through the Content API, including a hosted article URL when supplied.
- OAuth Bearer tokens supplied by the application.
- Dow Jones service-account OAuth with the documented two-step token exchange and automatic refresh.
- Transitional `user-key` authentication for customers whose contracts still permit it.
- Pagination, chronological delivery, bounded deduplication, rate-limit retries, and a two-minute overlap window for late-indexed stories.

## Authentication

For a token managed outside StockSharp, select `BearerToken` and set `Token`. For automatic OAuth, select `ServiceAccount` and set `ClientId`, `Login`, and `Password`; the connector obtains an identity token, exchanges it for the final API Bearer token, retains the refresh token, and renews the final token before expiry. `UserKey` sends `Token` in the legacy `user-key` header and exists only for the documented migration period.

`Address` defaults to `https://api.dowjones.com/`. `OAuthAddress` defaults to `https://accounts.dowjones.com/oauth2/v1/token`. Both addresses must use HTTPS.

## News subscriptions

The current Newswires Real-Time product documents `POST /content/realtime/search`; it does not publish a Newswires WebSocket contract. Live StockSharp subscriptions therefore poll that official endpoint at `PollingInterval`, advance a UTC publication cursor only after successful calls, overlap consecutive windows, and suppress duplicate article identifiers. The connector does not invent a WebSocket endpoint.

If a subscription contains a security code, the connector builds the documented `djn:djnabout:<ticker>` filter. An ISIN is used in the same documented taxonomy form when no ticker is supplied. `NewsQuery` can add any licensed Unified Query Language expression. Date filters use the documented `pdt` timestamps.

A history-only request without `From` uses `DefaultHistoryLookback`. `PageLimit` controls each REST page, while `MaxNewsItems` bounds a complete history request and each live poll. Results are emitted chronologically even when the API is queried newest-first to obtain the latest bounded history.

`IsFullTextEnabled` requests each result through `GET /content/{drn}`. If the contract permits search but not article retrieval, `403` and `404` responses fall back to the search snippet instead of failing the news subscription.

## Official documentation

- [Dow Jones Developer Platform](https://developer.dowjones.com/)
- [Newswires Real-Time APIs](https://developer.dowjones.com/documents/site-docs-newswires_apis-dow_jones_newswires_real_time_api)
- [Newswires Real-Time API](https://developer.dowjones.com/documents/site-docs-newswires_apis-dow_jones_newswires_real_time_api-real_time_search_api)
- [Single Article Retrieval](https://developer.dowjones.com/documents/site-docs-newswires_apis-dow_jones_newswires_real_time_api-real_time_content_api)
- [OAuth migration guide](https://developer.dowjones.com/documents/site-docs-newswires_apis-oauth-migration-guide)
- [Service Account Integration](https://developer.dowjones.com/documents/site-docs-getting_started-sessions_and_authentication-service_account_integration)
- [Interactive Real-Time API specification](https://developer.dowjones.com/swagger/realtime_api)
- [Interactive Content API specification](https://developer.dowjones.com/swagger/newswires_content_api)
- [StockSharp Dow Jones connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/dow_jones.html)

Dow Jones and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Dow Jones.
