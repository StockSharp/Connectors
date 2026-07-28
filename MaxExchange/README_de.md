# MAX-Exchange-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **MAX-Exchange-Konnektor** verbindet StockSharp mit der von der MaiCoin Group betriebenen taiwanischen Spotbörse. Er eignet sich besonders für Kryptomärkte in TWD und USDT.

## Hauptfunktionen

- Ermittlung von Spotinstrumenten einschließlich Handelsstatus, Genauigkeit und Mindestvolumen.
- Level-1-Kurse, Level-2-Orderbücher, öffentliche Trades und OHLCV-Kerzen.
- Echtzeit-Ticker, Orderbücher, Trades und Kerzen über WebSocket.
- Historische Kerzen und aktuelle Marktschnappschüsse über REST API v3.
- Kontostände, offene und historische Orders sowie eigene Ausführungen.
- Market-, Limit-, Stop-Market-, Stop-Limit-, Post-only- und IOC-Limit-Orders.
- Einzel- und Sammelstornierung sowie konfigurierbare REST- und WebSocket-Adressen.

## Typische Verwendung

Der Konnektor eignet sich für Handelsroboter, Terminals, TWD-Datensammler, Überwachungs- und Ordermanagementsysteme.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Konto- und Handelsoperationen erfordern API-Schlüssel und Secret von MAX Exchange.
