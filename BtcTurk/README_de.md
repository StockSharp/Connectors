# BtcTurk-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **BtcTurk-Konnektor** verbindet StockSharp mit BtcTurk Kripto, einer türkischen Spotbörse für Kryptowährungen. Er eignet sich für Handels- und Marktdatensysteme, die Märkte in TRY, BTC, USDT und weiteren Währungen über das einheitliche Nachrichtenmodell von StockSharp verwenden.

## Hauptfunktionen

- Abruf von Spotinstrumenten sowie Preis-, Mengen- und Ordergrenzen.
- Level-1-Kurse, Level-2-Orderbuch-Snapshots und öffentliche Trades.
- Echtzeit-Ticker, Orderbücher und Trades über WebSocket.
- Historische OHLCV-Kerzen für die von BtcTurk unterstützten Intervalle.
- Portfoliobestände, offene und historische Orders sowie Kontotrades.
- Aufgabe von Market-, Limit-, Stop-Market- und Stop-Limit-Orders.
- Stornierung einzelner Orders oder von Ordergruppen.
- Konfigurierbare REST-, Historien- und WebSocket-Endpunkte.

## Typische Verwendung

Der Konnektor kann in Handelsrobotern, Terminals, Datensammlern, Ordermanagement- und Überwachungssystemen für die Spotmärkte von BtcTurk Kripto eingesetzt werden.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Für Handel und Kontozugriff sind ein BtcTurk-API-Schlüssel und ein Base64-kodiertes Geheimnis mit passenden Rechten erforderlich. Bei Market-Kauforders versteht BtcTurk die Menge als Betrag in der Kurswährung; bei anderen Orders wird sie im Basiswert angegeben.
