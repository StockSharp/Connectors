# m.Stock-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **m.Stock-Konnektor** verbindet StockSharp mit einem indischen Broker und den von ihm unterstützten Börsensegmenten. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: indische Aktien, Indizes, Futures, Optionen, Währungsderivate, Fonds und Anleihen.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse, Tick-Trades, Orderbücher und Kerzen.
- Historische Kerzenabfragen für Charts, Analysen und Backtests.
- Vom Anbieter unterstützte Abläufe für Orderübermittlung, Änderung, Stornierung und Ausführung.
- Aktualisierungen von Portfolios, Salden, Positionen, Orders und Trades.
- Echtzeitabonnements über den Streaming-Transport des Anbieters.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Live-Strategien, Handelsterminals, Ordermanagement-Dienste und Überwachungswerkzeuge mit direktem Zugriff auf ein m.Stock-Konto.

Instrumente, Börsensegmente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von m.Stock, den Börsen und den Berechtigungen des verbundenen Kontos bestimmt.
