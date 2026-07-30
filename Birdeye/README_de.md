# Birdeye-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Birdeye-Konnektor** verbindet StockSharp mit den Birdeye-APIs für On-Chain-Kryptodaten. Er stellt Token-Suche, aktuelle Marktkennzahlen und OHLCV-Historien für eine ausgewählte Blockchain bereit; standardmäßig wird Solana verwendet.

## Wichtige Funktionen

- Token auf der ausgewählten Chain suchen und Referenzdaten laden.
- Die Suche auf eine Token-Adresse begrenzen und nach Mindestliquidität filtern.
- Level-1-Snapshots abrufen und per REST-Polling aktualisieren.
- Historische Zeitrahmenkerzen bis zum konfigurierten Verlaufslimit laden.
- Kostenpflichtiges WebSocket-Streaming für laufende Level-1- und Kerzenaktualisierungen aktivieren.
- Preise in US-Dollar oder in der nativen Währung der Chain darstellen.
- Von Birdeye unterstützte Intervalle nutzen; Kerzen unter einer Minute sind nur für Solana verfügbar.

## Typische Verwendung

Der Konnektor eignet sich für Token-Screening, die Beobachtung von On-Chain-Preisen und historische OHLCV-Analysen auf den von Birdeye unterstützten Netzwerken. Vor dem Abonnement werden Chain, API-Token, Notierungsmodus und optionale Suchfilter konfiguriert.

Birdeye ist ein Marktdatenanbieter; Orders, Portfolios, Ausführungen und Orderbücher sind daher nicht verfügbar. Historische Level-1-Ereignisse werden nicht unterstützt. Ohne Streaming endet ein Kerzenabonnement nach der historischen Antwort. Datenumfang, WebSocket-Zugang und Anfragegrenzen hängen vom Birdeye-API-Tarif ab.
