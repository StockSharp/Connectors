# SimFin-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **SimFin-Konnektor** bietet StockSharp schreibgeschützten Zugriff auf Unternehmensfundamentaldaten und tägliche Kurshistorien von SimFin. Datensätze werden auf Wertpapiere, Level-1-Snapshots, Tageskerzen und einen eigenen Fundamentaldatentyp abgebildet.

## Wichtige Funktionen

- Unternehmens- und Wertpapiersuche nach Ticker oder SimFin-Unternehmens-ID.
- Letzter verfügbarer Tagesdatensatz als Level-1-Snapshot.
- Historische tägliche OHLCV-Kerzen; keine Intraday-Intervalle oder Live-Kerzenaktualisierungen.
- Konfigurierbare Gewinn-und-Verlust-, Bilanz-, Cashflow- und abgeleitete Kennzahlensätze.
- Steuerung von Geschäftsperiode, Datumsbereich, standardisierten oder gemeldeten Werten, Kennzahlen und Datensatzmaximum.
- Nur endliche REST-Abonnements für Forschung und Historie; kein Streaming.
- Keine Tick-Trades, Orderbücher, Nachrichten, Portfolios oder Handelsoperationen.
- Authentifizierung, Drosselung und Formate von SimFin werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Fundamentalscreening, Bewertung, Tageskursanalyse und Backtests zusammen mit Ausführungs- oder Intraday-Daten eines anderen Konnektors.

Unternehmensabdeckung, Felder, Historie, Aktualisierungsfrequenz, Limits und Zugriff werden von SimFin und dem API-Tarif bestimmt.
