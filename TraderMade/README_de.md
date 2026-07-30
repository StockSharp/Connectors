# TraderMade-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **TraderMade-Konnektor** verbindet StockSharp mit den Devisen- und Kryptowährungsmarktdaten von TraderMade. REST-Historie und WebSocket-Kurse werden auf das einheitliche StockSharp-Marktdatenmodell abgebildet.

## Wichtige Funktionen

- Paarermittlung aus der Währungsliste und konfigurierten Kurswährungen oder aus einer expliziten Symbolliste.
- Echtzeit-Level-1-Geld-, Brief- und Mittelkurse über die Streaming-API.
- Optionale TraderLadder-Orderbuchdaten, wenn das Konto berechtigt und die Funktion aktiviert ist.
- Historische Zeitrahmenkerzen über REST, optional mit Kryptowährungsdaten am Wochenende.
- Getrennte REST- und Streaming-Schlüssel für reine Historie, reines Streaming oder kombinierte Nutzung.
- Kerzenabonnements sind endliche historische Anfragen; keine Live-Kerzen oder Tick-Trades.
- Reiner Marktdatenkonnektor ohne Portfolios, Salden oder Orderfunktionen.
- TraderMade-Symbole, Transporte und Formate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Devisen- und Krypto-Dashboards, Live-Kursüberwachung, Charts, Analysen und Backtests ohne Broker-Ausführung.

Paare, TraderLadder-Tiefe, Intervalle, Historie, Limits, Wochenenddaten und Streaming werden von TraderMade und dem API-Tarif bestimmt.
