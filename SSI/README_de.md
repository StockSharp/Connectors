# SSI-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **SSI-Konnektor** verbindet StockSharp über SSI FastConnect API v3 mit dem vietnamesischen Wertpapiermarkt. Er überführt SSI-Marktdaten und Brokerage-Vorgänge in das einheitliche StockSharp-Nachrichtenmodell.

## Wichtige Funktionen

- Suche nach Wertpapieren und Indizes von HOSE, HNX und UPCOM, einschließlich Aktien und unterstützten Futures.
- Echtzeit-Level-1-Kurse, Tick-Trades und Orderbücher mit anfänglichen REST-Snapshots, sofern verfügbar.
- Historische Zeitrahmenkerzen mit anschließenden Streaming-Aktualisierungen für unterstützte Intervalle.
- Übermittlung, Ersetzung und Stornierung einzelner Orders einschließlich SSI-spezifischer Bedingungen.
- Kontensuche sowie Salden-, Positions-, Order- und Ausführungsdaten über Streaming und regelmäßigen Abgleich.
- Konfigurierbare REST- und WebSocket-Endpunkte sowie Portfolio-Abfrageintervalle.
- FastConnect-Zugangsdaten sind erforderlich; der Handel benötigt zusätzlich Client-ID, Konto, privaten RSA-Schlüssel und aktuelles OTP.
- SSI-Sitzungen, Formate und Stream-Themen werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für vietnamesische Handelsterminals, Live-Strategien, Ordermanagement und Überwachung mit direktem SSI-Brokerzugang.

Instrumente, Historientiefe, Handelsrechte, Limits und Verfügbarkeit werden von SSI und den Rechten des verbundenen FastConnect-Kontos bestimmt.
