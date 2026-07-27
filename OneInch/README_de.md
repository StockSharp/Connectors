# 1inch-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **1inch-Konnektor** verbindet StockSharp mit einem On-Chain-Handels- und Liquiditätsprotokoll. Er übersetzt anbieterspezifische Daten und Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen dieselben Abonnements und Abläufe für verschiedene Handelsplätze verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: On-Chain-Vermögenswerte und Liquiditätspools.
- Instrumentensuche und Referenzdaten des Anbieters.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse.
- Vom Anbieter unterstützte Übermittlung von Swaps oder Blockchain-Transaktionen.
- Aktualisierungen von Portfolios, Salden, Positionen und Ausführungsstatus.
- Anbieterspezifische Transporte, Sitzungen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Live-Strategien, Handelsterminals, Ordermanagement-Dienste und Überwachungswerkzeuge mit direktem Anbieterzugang.

Verfügbare Netzwerke, Pools, Instrumente und Transaktionen hängen von 1inch, den konfigurierten RPC- oder Indexdiensten und den Wallet-Berechtigungen ab.
