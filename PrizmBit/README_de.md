# PrizmBit-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **PrizmBit-Konnektor** verbindet StockSharp mit einer veralteten Integration für eine Börse digitaler Vermögenswerte. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

Der ursprüngliche Dienst ist möglicherweise nicht mehr verfügbar. Die Integration bleibt für Kompatibilität, die Pflege bestehender Systeme und das Studium einer vollständigen Konnektorimplementierung erhalten.

## Wichtige Funktionen

- Typische Abdeckung: digitale Vermögenswerte.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse, Tick-Trades, Orderbücher, Kerzen und Orderlog-Ereignisse.
- Vom Anbieter unterstützte Abläufe für Orderübermittlung und Ausführungen.
- Aktualisierungen von Portfolios, Salden, Positionen und Ausführungsstatus.
- Echtzeitabonnements über den Streaming-Transport des Anbieters.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet zur Pflege einer bestehenden Integration oder als praktischer Quellcode zum Erlernen der Abbildung von Marktdaten, Transaktionen und Protokolldetails in StockSharp.

Vor dem produktiven Einsatz muss geprüft werden, ob die ursprüngliche API und die benötigten Endpunkte noch verfügbar sind.
