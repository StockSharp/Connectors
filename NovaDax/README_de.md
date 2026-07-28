# NovaDAX-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **NovaDAX-Konnektor** verbindet StockSharp mit dem Spot-Kryptomarkt von NovaDAX. Durch den Schwerpunkt der Börse auf Handelspaaren mit dem brasilianischen Real eignet er sich für Marktbeobachtung, Datenerfassung und automatisierten Handel im brasilianischen Kryptomarkt.

## Wichtigste Funktionen

- Ermittlung von Spot-Instrumenten mit Handelsstatus, Preis- und Mengenpräzision sowie Mindestauftragswerten.
- Level-1-Kurse, Level-2-Orderbücher, öffentliche Trades und historische OHLCV-Kerzen.
- Echtzeit-Ticker, Markttiefe und Trades über Socket.IO.
- Marktschnappschüsse, letzte Trades und historische Kerzen über REST.
- Salden, aktive und historische Aufträge, Auftragsstatus und eigene Ausführungen.
- Market-, Limit-, Stop-Market- und Stop-Limit-Aufträge mit Einzelstornierung und Stornierung je Instrument.
- Konfigurierbare REST- und Socket.IO-Adressen, Unterkonto-ID und Engine.IO-Protokollversion.

Öffentliche Marktdaten sind ohne Zugangsdaten verfügbar. Portfolio- und Handelsfunktionen erfordern einen NovaDAX-API-Schlüssel und ein Secret; bei Bedarf kann eine Unterkonto-ID angegeben werden.
