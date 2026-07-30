# Pendle-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Pendle-Konnektor** verbindet StockSharp mit einem On-Chain-Protokoll für den Handel mit Renditen. Er übersetzt Protokolldaten und Wallet-Vorgänge in das einheitliche StockSharp-Nachrichtenmodell, sodass Anwendungen standardisierte Abonnements und Transaktionsabläufe für Pendle-Märkte verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: verzinsliche On-Chain-Vermögenswerte, Principal-Token, Yield-Token und Pendle-Märkte.
- Instrumentensuche und Referenzdaten des Protokolls.
- Vom Adapter unterstützte Marktdaten: Level-1-Kurse und Kerzen.
- Historische Kerzenabfragen und fortlaufende Marktdatenaktualisierungen für Charts, Analysen und Strategieabläufe.
- Vom Anbieter unterstützte Token-Konvertierung und Blockchain-Transaktionen einschließlich erforderlicher Token-Freigaben.
- Aktualisierungen von Wallet-Portfolio, Salden, Positionen und Ausführungsstatus.
- Protokollspezifischer HTTP- und RPC-Transport, Wallet-Transaktionen und Datenformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für die Überwachung von Renditemärkten, Live-Strategien, Wallet-bezogene Handelswerkzeuge und Dienste, die über Pendle Kurse abrufen oder Konvertierungen ausführen.

Netzwerke, Märkte, Token, Kurse, Transaktionsfunktionen, Gebühren und Verfügbarkeit hängen von Pendle, den konfigurierten API- und RPC-Endpunkten, dem aktuellen Chain-Zustand und den Wallet-Berechtigungen ab.
