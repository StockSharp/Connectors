# Zonda-Crypto-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Zonda-Crypto-Konnektor** verbindet StockSharp mit der zentralisierten Kryptowährungs-Spotbörse zondacrypto. REST- und WebSocket-Daten sowie Kontovorgänge werden in das einheitliche StockSharp-Nachrichtenmodell übersetzt.

## Wichtige Funktionen

- Spotmarktsuche mit Währung, Preis- und Mengenschritten sowie Mindestbetrag.
- Echtzeit-Level-1, Tick-Trades und Orderbuch-Snapshots und -Aktualisierungen über öffentliche Streams.
- REST-Snapshots und verfügbare jüngste Trade-Historie vor der Live-Fortsetzung; keine Kerzendaten.
- Market- und Limit-Orders mit unterstützten GTC-, IOC-, FOK- und Post-only-Optionen.
- Einzelne oder gefilterte Gruppenstornierung sowie Order- und Ausführungsstatus; keine atomare Ersetzung.
- Wallet-Salden und Portfolios über private Streams mit periodischem REST-Abgleich.
- Private Vorgänge benötigen API-Schlüssel und Secret; öffentliche Daten funktionieren ohne Handelszugangsdaten.
- Authentifizierung, Marktcodes, Transporte, Filter und Formate werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für zondacrypto-Spotterminals, Live-Strategien, Analyse jüngster Trades, Kontoüberwachung und Ordermanagement.

Märkte, jüngste Historie, Handelsrechte, Orderoptionen, Limits und Verfügbarkeit werden von zondacrypto und dem Konto bestimmt.
