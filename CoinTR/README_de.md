# CoinTR-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinTR-Konnektor** verbindet StockSharp mit CoinTR, einer auf den türkischen Markt ausgerichteten Kryptobörse. Er stellt CoinTR-Spotinstrumente über das standardisierte StockSharp-Nachrichtenmodell bereit.

## Wichtigste Funktionen

- Abruf von Spotinstrumenten, Preis- und Mengenpräzision sowie Handelsgrenzen.
- Level-1-Kurse, Level-2-Orderbuch-Snapshots und öffentliche Trades.
- Echtzeit-Ticker, Orderbücher, Trades und Kerzen über WebSocket.
- Historische OHLCV-Kerzen für die von CoinTR unterstützten Intervalle.
- Portfoliosalden, aktive Orders und private Ausführungsbenachrichtigungen.
- Aufgabe von Market-, Limit- und Trigger-Orders sowie Orderstornierung.
- Konfigurierbare REST-, öffentliche und private WebSocket-Endpunkte.

## Typischer Einsatz

Der Konnektor eignet sich für Handelsroboter, Terminals, Marktdatensammler, Überwachungssysteme und Order-Management-Dienste für CoinTR-Spotmärkte.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Für Handel und Kontozugriff sind API-Schlüssel, Secret und Passphrase mit passenden Berechtigungen erforderlich. Bei einem Market-Kauf interpretiert CoinTR das Volumen als Betrag in der Notierungswährung; Limit-Orders und Market-Verkäufe verwenden die Menge des Basiswerts.
