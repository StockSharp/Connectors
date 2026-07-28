# Coinstore-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Coinstore-Konnektor** verbindet StockSharp mit dem Spot-Kryptomarkt von Coinstore. Er eignet sich zur Beobachtung des umfangreichen Listing-Markts und zur Automatisierung des Handels mit Krypto- und Stablecoin-Paaren.

## Wichtigste Funktionen

- Ermittlung von Spot-Instrumenten mit Handelsstatus, Preis- und Mengenpräzision sowie Mindestauftragsdaten.
- Level-1-Daten, Level-2-Orderbücher, öffentliche Trades und OHLCV-Kerzen.
- Echtzeit-Ticker, Markttiefe, Trades und Kerzen über WebSocket.
- Letzte Trades, Orderbuch-Snapshots und Kerzenhistorie über REST.
- Portfoliobestände, aktive Aufträge, Auftragsstatus und eigene Ausführungen.
- Market-, Limit-, Post-only- und IOC-Aufträge sowie Einzel- und Sammelstornierung.
- Konfigurierbare REST- und WebSocket-Adressen.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Portfolio- und Handelsfunktionen erfordern API-Schlüssel und Secret von Coinstore. Private Zustände werden über authentifizierte REST-Anfragen aktualisiert.
