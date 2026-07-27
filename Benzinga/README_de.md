# Benzinga-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Benzinga-Konnektor** verbindet StockSharp mit einem Dienst für Finanznachrichten und Ereignisdaten. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: Aktien, Futures, Optionen, Fonds und ETFs, Indizes.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse, Kerzen und Finanznachrichten.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Echtzeitabonnements über den Streaming-Transport des Anbieters.
- Dieser Adapter ist für Marktdaten vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet, um Nachrichten und Ereignisse des Anbieters in Überwachung, Analyse, Benachrichtigungen und ereignisgesteuerte Strategien einzubinden.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von Benzinga, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
