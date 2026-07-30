# MarketData.app-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **MarketData.app-Konnektor** verbindet StockSharp mit einem professionellen Marktdatendienst. Er übersetzt anbieterspezifische Daten in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Anfragen und Abläufe für verschiedene Datenquellen verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: Aktien, ETFs, Optionen, Indizes und Fonds.
- Instrumentensuche einschließlich Optionsketten sowie Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kursschnappschüsse und Kerzen.
- Historische Kerzenabfragen für Charts, Analysen und Backtests; der Dienst stellt keine Optionskerzen bereit.
- Dieser Adapter ist für Marktdaten vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Charts, Instrumenten- und Optionssuche, Marktdatenspeicher, Analysen, Research-Abläufe und Strategietests mit Anbieterdaten.

Instrumente, Historientiefe, Anpassungen, Limits, Datenrechte und Verfügbarkeit werden von MarketData.app und dem verbundenen API-Tarif bestimmt.
