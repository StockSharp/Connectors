# DexScreener-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **DexScreener-Konnektor** bindet über die öffentliche REST-API von DexScreener kettenübergreifende Analysedaten zu Handelspaaren dezentraler Börsen in StockSharp ein. Der Marktdatenadapter arbeitet ausschließlich lesend und benötigt keine API-Zugangsdaten.

## Wichtige Funktionen

- Paarsuche nach Chain-ID, Token-Adresse, genauer Paaradresse oder Freitext unter Berücksichtigung der StockSharp-Grenzen für Überspringen und Anzahl.
- Level-1-Snapshots mit dem letzten Preis in USD und im nativen Token, 24-Stunden-Volumen und -Preisänderung, Liquidität und Handelsstatus.
- Regelmäßige REST-Aktualisierung aktiver Level-1-Abonnements; das Intervall ist konfigurierbar und beträgt standardmäßig 30 Sekunden.
- Abdeckung der von DexScreener indexierten Blockchains und Liquiditätspools.
- Öffentlicher Zugriff ohne API-Schlüssel oder private Kontositzung.
- Keine historischen Level-1-Ereignisse und kein Echtzeit-Streaming.
- Keine Tick-Trades, Orderbücher, Kerzen, Auftragserteilung, Portfoliodaten oder Kontovorgänge.

## Typische Verwendung

Verwenden Sie diesen Konnektor für die Suche nach DEX-Paaren, Beobachtungslisten, Liquiditätsfilter und Dashboards mit regelmäßig aktualisierten aggregierten Marktkennzahlen.

Er ist weder ein Ausführungskonnektor noch eine Quelle für backtestingfähige Ereignishistorien. Paarabdeckung, verfügbare Felder, Datenaktualität und Anfragelimits werden von DexScreener und den zugrunde liegenden dezentralen Handelsplätzen bestimmt.
