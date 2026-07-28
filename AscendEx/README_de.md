# AscendEX-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **AscendEX-Konnektor** verbindet StockSharp mit der veröffentlichten AscendEX Pro API. Ein Adapter deckt Cash-Spot-, Margin- und unbefristete Futures-Märkte ab und eignet sich damit für marktübergreifende Kryptostrategien sowie zur Bewahrung des dokumentierten Börsenprotokolls.

## Hauptfunktionen

- Ermittlung von Spot-, Margin- und Perpetual-Futures-Instrumenten mit Handelsstatus, Preis- und Mengenschritt sowie Ordergrenzen.
- Level-1-Kurse, Level-2-Orderbücher, öffentliche Trades und OHLCV-Kerzen.
- REST-Snapshots und Historie sowie getrennte Echtzeit-WebSockets für Spot und Futures.
- Cash- und Margin-Salden, Futures-Sicherheiten und -Positionen, offene und historische Orders sowie Ausführungen.
- Market-, Limit-, Stop-Market- und Stop-Limit-Orders mit GTC, IOC, FOK, Post-only und Reduce-only für Futures.
- Einzelne und gebündelte Orderstornierung.
- Konfigurierbare REST-, Spot-WebSocket- und Futures-WebSocket-Adressen, Kontogruppe und Cash-/Margin-Modus.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Für Portfolio- und Handelsfunktionen sind API-Schlüssel, Geheimnis und die von AscendEX zugewiesene Kontogruppe erforderlich.
