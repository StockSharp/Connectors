# BitoPro-Connector

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **BitoPro-Connector** verbindet StockSharp mit BitoPro, einer regulierten, auf Taiwan ausgerichteten Kryptobörse mit aktiven TWD-Spotmärkten.

## Hauptfunktionen

- Abruf von Spotinstrumenten, Preis- und Mengenpräzision sowie Handelsgrenzen.
- Level-1-Daten, Level-2-Orderbuch-Snapshots und öffentliche Trades.
- Echtzeit-Ticker, Orderbücher und Trades über WebSocket.
- Historische OHLCV-Kerzen für alle von BitoPro angebotenen Intervalle.
- Kontostände, offene und historische Orders sowie eigene Trade-Historie.
- Limit-, Market-, Stop-Limit- und Post-only-Orders sowie Einzel- und Sammelstornierung.
- Konfigurierbare REST- und WebSocket-Adressen.

## Typische Verwendung

Geeignet für Handelsroboter, Terminals, TWD-Marktdatensammler, Überwachungs- und Orderverwaltungssysteme.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Für Konto- und Handelsfunktionen sind E-Mail, API-Schlüssel und Secret erforderlich. Bei Market-Käufen erwartet BitoPro den Betrag in der Quotierungswährung; der Connector rechnet das StockSharp-Basisvolumen mit dem letzten öffentlichen Preis um.
