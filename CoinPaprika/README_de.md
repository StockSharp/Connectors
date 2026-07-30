# CoinPaprika-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinPaprika-Konnektor** verbindet StockSharp mit der CoinPaprika-API für Kryptomarktdaten. Er stellt globale Coin-Referenzdaten oder Märkte einer ausgewählten Börse sowie Ticker-Snapshots und historische OHLCV-Kerzen bereit.

## Wichtige Funktionen

- CoinPaprika-Coins global suchen oder Instrumente auf eine konfigurierte Börse beschränken.
- Die Notierungswährung für Ticker- und Kerzenanfragen wählen.
- Level-1-Snapshots mit Preis, 24-Stunden-Volumen, Veränderung und verfügbarem Marktstatus empfangen.
- Level-1-Werte per REST-Polling in einem konfigurierbaren Intervall aktualisieren.
- Historische OHLCV-Zeitrahmenkerzen laden.
- Die kostenlose API ohne Token oder mit Token den professionellen Endpunkt und erweiterte Berechtigungen nutzen.
- Historische Antworten auf ein konfiguriertes Maximum von 366 Datensätzen begrenzen.

## Typische Verwendung

Der Konnektor eignet sich für Krypto-Referenzdaten, einfache Preisüberwachung und historische OHLCV-Analysen. Vor der Abfrage werden globale oder börsenspezifische Suche und die Notierungswährung gewählt.

CoinPaprika ist ein Datenaggregator und kein Handelsplatz. Der Adapter bietet keine Orders, Portfolios, Tick-Trades oder Markttiefe. Historische Level-1-Ereignisse und Live-Kerzenaktualisierungen sind nicht verfügbar. Intraday-Historie, Abdeckung, Antwortgröße und Ratenlimits hängen vom CoinPaprika-API-Tarif und Token ab.
