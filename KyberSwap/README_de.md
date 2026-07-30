# KyberSwap-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **KyberSwap-Konnektor** verbindet StockSharp mit der KyberSwap Aggregator API v1 und EVM-Netzwerken. Er stellt konfigurierte Tokenpaare als StockSharp-Instrumente bereit, leitet ausführbare Kurse aus Aggregator-Routen ab und sendet signierte On-Chain-Swaps.

## Wichtige Funktionen

- Suche nach konfigurierten Tokenpaaren und Laden ihrer Metadaten auf Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche und Linea.
- Level-1-Geld- und Briefkurse aus ausführbaren Aggregator-Routen für ein konfigurierbares Probevolumen.
- Regelmäßiges REST-Polling aktiver Level-1-Abonnements; historische Kursereignisse und Streaming sind nicht verfügbar.
- Sofortige Markt-Swaps, lokal signiert und über EVM-JSON-RPC gesendet, mit konfigurierbarer Slippage und automatischer Token-Freigabe.
- Wallet-Token-Guthaben und Portfolioaktualisierungen über Blockchain-Abfragen.
- Verfolgung der vom Konnektor gesendeten Swaps anhand des Transaktionshashes, bis ein EVM-Beleg Erfolg oder Fehler bestätigt.
- Keine Tick-Trades, Orderbücher, Kerzen, Limit-Aufträge oder Änderung und Stornierung bereits gesendeter Transaktionen.

## Typische Verwendung

Verwenden Sie diesen Konnektor für routenbewusste DEX-Kursüberwachung und automatisierte Markt-Swaps in den unterstützten EVM-Netzwerken.

Kurse können ohne Handelszugangsdaten abgefragt werden; die Ausführung benötigt jedoch Wallet, privaten Schlüssel und einen funktionsfähigen RPC-Endpunkt. Tokendefinitionen, Routenliquidität, Freigaben, Gas-Kosten, Slippage, Beleglatenz, API-Limits und Netzwerkzustand beeinflussen jeden Swap.
