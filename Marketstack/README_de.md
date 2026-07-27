# Marketstack-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Marketstack-Konnektor** verbindet StockSharp mit einem professionellen Markt- und Analysedienst. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: Aktien.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse und Kerzen.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Dieser Adapter ist für Marktdaten vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Charts, Marktdatenspeicher, Analysen, Forschung und Strategietests mit Daten des Anbieters.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von Marketstack, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
