# Tokocrypto-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Tokocrypto-Konnektor** verbindet StockSharp mit dem MAIN-Spotmarkt von Tokocrypto. Er eignet sich für den indonesisch ausgerichteten Kryptohandel und für Anwendungen, die Tokocrypto-Marktdaten im StockSharp-Nachrichtenmodell benötigen.

## Hauptfunktionen

- Ermittlung der MAIN-Spotinstrumente mit Preis-, Volumen- und Mindestorderfiltern.
- Level-1-Kurse, Level-2-Orderbücher, öffentliche Trades und OHLCV-Kerzen.
- Live-Ticker, partielle Orderbücher, Trades und Kerzen über WebSocket.
- Historische Kerzen und aktuelle Marktschnappschüsse über die öffentliche REST-API.
- Spotguthaben, offene und historische Orders sowie eigene Ausführungen.
- Market-, Limit-, Stop-Market-, Stop-Limit-, Post-only-, IOC- und FOK-Orders.
- Einzel- und Gruppenstornierung; Account-REST-, Marktdaten-REST- und WebSocket-Adressen sind konfigurierbar.

## Typische Verwendung

Der Konnektor kann in Handelsrobotern, Terminals, Marktdatensammlern, Überwachungsdiensten und Ordermanagementsystemen für Tokocrypto-Spotinstrumente eingesetzt werden.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Konto- und Handelsfunktionen erfordern einen Tokocrypto-API-Schlüssel und ein Secret.
