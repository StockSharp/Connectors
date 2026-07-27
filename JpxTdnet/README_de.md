# JPX TDnet-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **JPX TDnet-Konnektor** verbindet StockSharp mit einem Dienst für Finanzdaten und Referenzinformationen. Er übersetzt anbieterspezifische Daten in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Datenquellen verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: Aktien und Emittentenreferenzdaten.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Anbieter unterstützte Markt-, Unternehmens-, Einreichungs-, Offenlegungs- und Referenzdaten.
- Vom Adapter unterstützte Marktdaten: Finanznachrichten und Finanzveröffentlichungen.
- Historische Datenabfragen für Charts, Analysen und Backtests.
- Dieser Adapter ist für den Datenzugriff vorgesehen und leitet keine Orders weiter.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Wertpapierstammdaten, Offenlegungsüberwachung, Emittentenrecherche, Compliance-Abläufe und historische Analysen.

Instrumente, Datentiefe, Handelsrechte, Limits und Verfügbarkeit werden von JPX TDnet, dem API-Tarif und den Berechtigungen des verbundenen Kontos bestimmt.
