# Mexc-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Mexc-Konnektor** verbindet StockSharp mit einer zentralisierten Börse für digitale Vermögenswerte. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: digitale Vermögenswerte, Spotmärkte, Derivate.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse, Tick-Trades und Orderbücher.
- Vom Anbieter unterstützte Abläufe für Orderübermittlung und Ausführungen.
- Aktualisierungen von Portfolios, Salden, Positionen und Ausführungsstatus.
- Echtzeitabonnements über den Streaming-Transport des Anbieters.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Live-Strategien, Handelsterminals, Ordermanagement-Dienste und Überwachungswerkzeuge mit direktem Anbieterzugang.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von Mexc, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
