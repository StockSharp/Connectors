# OkexHistory-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **OkexHistory-Konnektor** verbindet StockSharp mit einem Markt- und Analysedienst für digitale Vermögenswerte. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: digitale Vermögenswerte.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Tick-Trades, Orderbücher und Kerzen.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Dieser Adapter ist für Marktdaten vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Charts, Marktdatenspeicher, Analysen, Forschung und Strategietests mit Daten des Anbieters.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von OkexHistory, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
