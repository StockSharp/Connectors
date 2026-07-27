# Zero Hash-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Zero Hash-Konnektor** verbindet StockSharp mit einem Verwahrungs-, Abwicklungs- oder institutionellen Dienst für digitale Vermögenswerte. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: digitale Vermögenswerte, Forex und CFDs.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse, Tick-Trades und Orderbücher.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Vom Anbieter unterstützte Konto-, Transfer- und Transaktionsabläufe.
- Aktualisierungen von Portfolios, Salden, Positionen und Ausführungsstatus.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Live-Strategien, Handelsterminals, Ordermanagement-Dienste und Überwachungswerkzeuge mit direktem Anbieterzugang.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von Zero Hash, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
