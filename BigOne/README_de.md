# BigONE-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **BigONE-Konnektor** verbindet StockSharp mit den Spot- und Kontraktmärkten von BigONE. Ein Adapter deckt normale Kryptopaare sowie coin- und USDT-besicherte unbefristete Kontrakte ab.

## Hauptfunktionen

- Ermittlung von Spotpaaren und verfügbaren unbefristeten Kontrakten.
- Level-1-Kurse, Orderbücher, öffentliche Trades und OHLCV-Kerzen.
- Spot-Streams über JSON WebSocket und eigene URL-Streams für Kontrakte.
- Historische Spot-Kerzen und aktuelle REST-Snapshots beider Marktarten.
- Spot- und Kontraktsalden, Kontraktpositionen, Orders und eigene Trades.
- Market-, Limit-, IOC-, FOK-, Post-only-, Spot-Stop- und Reduce-only-Kontraktorders.
- Stornierung einzelner Orders und Ordergruppen.
- Konfigurierbare Adressen für Spot-/Kontrakt-REST sowie öffentliche und private WebSockets.

## Einsatz

Der Konnektor eignet sich für Handelsroboter, Terminals, Marktdatensammler, Überwachung und Ordermanagement mit BigONE-Spotliquidität und Derivaten.

Öffentliche Marktdaten benötigen keine Zugangsdaten. Konto- und Handelsfunktionen erfordern einen BigONE-API-Schlüssel und ein Secret.
