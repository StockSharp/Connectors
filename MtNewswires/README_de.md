# MT Newswires-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **MT Newswires-Konnektor** verbindet StockSharp mit einem Dienst für Finanznachrichten und Ereignisdaten. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: Aktien.
- Vom Adapter unterstützte Marktdaten: Finanznachrichten.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Dieser Adapter ist für Marktdaten vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet, um Nachrichten und Ereignisse des Anbieters in Überwachung, Analyse, Benachrichtigungen und ereignisgesteuerte Strategien einzubinden.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von MT Newswires, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
