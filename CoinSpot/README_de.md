# CoinSpot-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinSpot-Konnektor** verbindet StockSharp mit der CoinSpot-Spotbörse und dem Kryptobroker. Für Marktdaten, Kontostatus und Ordervorgänge nutzt er die öffentlichen, Handels- und privaten schreibgeschützten REST-APIs von CoinSpot.

## Wichtige Funktionen

- CoinSpot-Spotmärkte und Instrumentmetadaten suchen.
- Level-1-Ticker-, Orderbuch-Snapshots und aktuelle Tick-Trades abrufen.
- Öffentliche Abonnements per REST-Polling in einem konfigurierbaren Intervall aktualisieren.
- Limit- und Market-Kauf- oder Verkaufsorders aufgeben.
- Einzelne Orders oder Gruppen passender offener Orders stornieren.
- Salden, Portfoliostatus, offene und historische Orders sowie eigene Trades laden.
- Öffentliche, Handels- und private schreibgeschützte API-Endpunkte getrennt konfigurieren.

## Typische Verwendung

Der Konnektor eignet sich zur Beobachtung des CoinSpot-Spotmarkts und für REST-basierten automatisierten Handel. Öffentliche Marktdaten benötigen keine Authentifizierung; Konto- und Orderfunktionen erfordern CoinSpot-API-Schlüssel und Secret mit passenden Berechtigungen.

Der Adapter besitzt keinen WebSocket-Datenstrom und bietet weder Kerzen noch historische Level-1-Ereignisse oder Orderbücher. Öffentliche Daten werden abgefragt; die Historie aktueller Trades ist durch die Anbieterantwort begrenzt. Atomarer Ersatz sowie bedingte, Iceberg-, Post-only- und GTD-Orders werden nicht unterstützt.
