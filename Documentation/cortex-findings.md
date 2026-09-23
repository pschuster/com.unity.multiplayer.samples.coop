# ODIN Cortex — Findings aus der Boss-Room-Integration

Stand: 2026-09-21. Basis: die Cortex-Function dieses Repos (`Backend/cortex-function/`), der
Unity-Client (`Assets/Scripts/OdinServices/`) und das veröffentlichte API-Schema von
`https://cortex.odin.4players.io/api/docs-json`.

Der Zweck dieses Dokuments ist **nicht**, Workarounds im Sample zu planen, sondern die Stellen zu
benennen, an denen Cortex das Spiel zu Workarounds zwingt.

> Belege aus dem OpenAPI-Schema beschreiben den *veröffentlichten Vertrag*. NestJS dokumentiert nur
> annotierte Query-Parameter, das tatsächliche Verhalten kann abweichen. Die mit ⚠ markierten Punkte
> sollten gegen die laufende API gegengeprüft werden, bevor daraus Tickets werden.

---

## Die drei Kernprobleme

1. **Moderation ohne Durchsetzung.** Cortex erkennt, bewertet und legt Sanctions an — kann sie aber
   nicht durchsetzen. Ein Sanction ist ein Datensatz ohne Wirkung.
2. **Kein Weg von Cortex zum Spiel-Client.** Es gibt keinen Kanal, über den ein Client erfährt, dass
   etwas passiert ist. Weder Push noch ein Credential, mit dem er selbst zuhören dürfte.
3. **Die Read-APIs sind nicht für Live-Betrieb gebaut.** Wer sie trotzdem live nutzt, pollt — und
   zieht dabei jedes Mal den vollen Datenbestand.

Alles Weitere in diesem Dokument ist Detail oder Folge davon.

---

## A — Moderation ist nicht durchsetzbar

### A1. Ein Sanction hat keine Wirkung (kritisch)

**Beobachtung.** Der Profanity-Filter flaggt und legt einen Sanction an. Danach passiert nichts.
Es gibt keine Schnittstelle, über die dieser Sanction auf die Voice-Verbindung wirkt.

**Beleg.** `POST /api/projects/{projectId}/token` nimmt ausschließlich `roomId` und `userId`
entgegen (`GenerateProjectTokenRequest`). Keine Capabilities, keine Kanal-Beschränkung, kein
Ablaufdatum unter Kontrolle des Aufrufers. Ein ausgestellter Token erlaubt immer alles.

**Auswirkung.** Das Spiel muss selbst durchsetzen. Ein Client-seitiges Mute ist durch einen
gepatchten Client umgehbar — die Audio-Pakete gehen P2P durch den ODIN-Room, nicht über den
Game-Server. Wer moderieren *muss* (Jugendschutz, DSA, Plattform-Anforderungen), kann sich auf
Cortex derzeit nicht berufen.

**Vorschlag**, in aufsteigender Ambition:

- **(a) Token-Capabilities.** `POST /token` akzeptiert `capabilities: { canPublishAudio, channels[] }`
  und `expiresIn`. Cortex leitet sie beim Ausstellen aus den aktiven Sanctions des Participants ab.
  Billig, und schon das allein macht aus „Client bittet um Token" einen Enforcement-Punkt.
- **(b) Token-Revocation.** Ein laufender Sanction invalidiert ausgestellte Tokens, der Client muss
  neu holen. Braucht kurze Token-Lebensdauer.
- **(c) Room-seitiges Enforcement.** Cortex ist über den Transkriptions-Bot ohnehin im Room. Wenn
  ODIN Voice serverseitig „Peer X darf nicht publishen" kann, ist das der einzige wirklich dichte
  Weg. Zu klären, ob ODIN Voice das heute hergibt — das ist möglicherweise gar kein Cortex-Thema.

Für ein glaubwürdiges Moderations-Feature braucht es mindestens (a).

### A2. Sanctions sind nicht so abfragbar, wie ein Spiel sie braucht

**Beleg.** `GET /api/projects/{projectId}/sanctions` filtert nur nach `type`, `activeOnly`, `limit`,
`offset`. Es gibt `…/sanctions/participant/{participantId}` und `…/sanctions/user/{externalUserId}/active`
— beide für genau *einen* Spieler.

**Auswirkung.** Für eine Lobby mit 8 Spielern: entweder 8 Requests, oder ein projektweiter Abruf mit
anschließendem Filtern im Speicher der Function. Bei wachsendem Projekt skaliert Letzteres nicht,
weil `limit`/`offset` über *alle* Sanctions des Projekts laufen.

**Vorschlag.** `participantIds` (Liste), `sessionId` und `gatheringId` als Filter. Noch besser:
`GET /gatherings/{id}/sanctions` — das Gathering ist die Einheit, in der ein Spiel denkt, und die
Autorisierung („bin ich Member?") ist dort natürlich definiert.

### A3. `sanctions` fehlt im Scope-Modell ⚠

**Beleg.** Dokumentierte API-Key-Scopes: `sessions`, `messages`, `plugins`, `plugin-endpoints`,
`gatherings`, `functions`. Die Sanctions-Endpunkte tauchen in keinem davon auf.

**Auswirkung.** Unklar, welcher Scope für Sanctions nötig ist. Entweder sind sie ungeschützt, oder
ein Integrator kann keinen Key mit minimalen Rechten ausstellen. Beides ist schlecht.

**Vorschlag.** `sanctions:read` / `sanctions:write` einführen und in der Scope-Tabelle dokumentieren.
Der Unterschied ist relevant: Ein Game-Backend will lesen und durchsetzen, aber nicht zwingend
selbst sanktionieren dürfen.

### A4. Keine Eskalations-Policy

**Beobachtung.** Was der Filter anlegt (Typ, Dauer, Verhalten bei Wiederholung) ist aus der API nicht
ersichtlich, und das Sample kann daher keine sinnvolle Reaktion bauen. Ob `endAt` überhaupt gesetzt
wird, ist offen — ohne `endAt` gibt es kein „Mute für 5 Minuten", nur „für immer".

**Vorschlag.** Eskalationsleiter als Plugin-Setting: 1. Verstoß `warn`, 2. `mute` 5 min, 3. `mute`
30 min, 4. `temp_ban`. Zählfenster konfigurierbar. `endAt` immer setzen, auch bei `warn`
(= „Warnung im UI für 30 s zeigen"). Das ist die Funktion, für die Kunden die Moderation kaufen —
ein roher Sanction-Datensatz ist Halbzeug.

---

## B — Es gibt keinen Weg von Cortex zum Spiel-Client

### B1. SSE ist für Clients nicht nutzbar (kritisch)

**Beleg.** `GET /api/projects/{projectId}/events/stream` verlangt `api-key` oder `bearer`. Die
`securitySchemes` kennen genau zwei Dinge: Dashboard-JWT und `X-API-Key`. Ein projekt-gescopter
API-Key in einem Spiel-Client ist ein ausgelieferter Generalschlüssel.

**Nebenbei zur verbreiteten Annahme „SSE geht in Unity nicht":** Das stimmt so nicht.
`UnityWebRequest` puffert per Default die ganze Antwort, mit `DownloadHandlerScript` bekommt man
Chunks; `System.Net.Http.HttpClient` mit `ResponseHeadersRead` + `ReadAsStreamAsync` streamt auf
Mono und IL2CPP sauber. Praktisch kaputt ist es nur unter WebGL. **Das Auth-Modell ist der Blocker,
nicht die Client-Technik.** Das ist eine gute Nachricht: Es ist in Cortex lösbar.

### B2. Functions können nicht vermitteln

**Beleg.** Der Invoke-Proxy ist Request/Response, Timeout default 30 s, max 300 s. Eine Function
kann keinen Stream halten und keinen proxen.

**Auswirkung.** Der naheliegende Ausweg „Function als Broker zwischen SSE und Client" existiert nicht.

### B3. Kein Push in den ODIN-Room

**Beobachtung.** Ich habe die vollständige Pfadliste durchgesehen: Es gibt keinen Endpunkt, um eine
Nachricht in einen ODIN-Room zu senden. Dabei sitzt der Cortex-Bot bereits als Peer in genau dem
Room, in dem alle Spieler hängen, und ODIN-Rooms haben einen Datenkanal
(`Room.SendMessage` / `OnMessageReceived`, im Unity-SDK vorhanden und vom NGO-Transport nicht belegt).

**Vorschlag.** `POST /projects/{id}/rooms/{roomId}/messages` mit optionalem `targetUserId`. Damit
wird der Bot vom reinen Zuhörer zum Rückkanal. Für Spiele ist das der mit Abstand natürlichste
Push-Weg: Die Verbindung steht schon, sie ist authentifiziert, sie ist niedrig-latent, und sie
braucht kein neues Credential. Ein `sanction.created` würde beim Betroffenen in Millisekunden
ankommen, statt im nächsten Poll-Fenster.

### B4. Folge: Poll-Verstärkung

Ohne Push pollt jeder Client einzeln. Im aktuellen Sample: alle 2 s pro Client. Bei 8 Spielern sind
das 4 Requests/Sekunde pro Lobby — und jeder davon zieht das komplette Transkript (siehe C1).
Bei 100 parallelen Lobbies sind das 400 req/s für Daten, die sich vielleicht zehnmal pro Minute
ändern.

---

## C — Die Read-APIs sind nicht für Live-Polling gebaut

### C1. `GET /sessions/{id}/messages` hat keine Filter ⚠

**Beleg.** Im OpenAPI-Schema hat der Endpunkt außer `projectId` und `id` **keinerlei** Parameter.
Die Skill-Dokumentation behauptet `participantId` und Pagination — das ist entweder Doku-Drift oder
fehlende Annotation, und genau diese Unsicherheit ist schon das Problem.

**Auswirkung.** Unsere Function holt bei *jedem* Poll alle Nachrichten der Session, filtert im
Speicher nach Timestamp und schneidet auf die letzten 50 zu
(`Backend/cortex-function/bossroom-backend.js`, Funktion `transcript`). Eine 60-minütige Session mit
2000 Zeilen wird alle 2 Sekunden pro Client komplett übertragen.

**Vorschlag.** `since` (Timestamp oder Message-ID als Cursor), `limit`, `order`. Zusätzlich ETag
bzw. `If-None-Match`, damit ein unveränderter Stand mit 304 beantwortet wird — das allein nimmt den
meisten Traffic raus, ohne dass ein Integrator irgendetwas ändern muss.

### C2. Annotationen kommen nicht mit den Nachrichten

**Beobachtung.** Für die Moderations-Flags braucht es einen zweiten Call auf
`POST /api/plugins/annotations/messages/batch`. Diese Route ist **nicht** projekt-gescopt, während
alles andere es ist.

**Auswirkung.** Doppelte Roundtrips im heißen Pfad, und eine Inkonsistenz im Autorisierungsmodell:
Ein Aufrufer reicht dort beliebige `messageIds` ein. Ob die Prüfung gegen den Tenant greift, ist von
außen nicht erkennbar — das gehört verifiziert.

**Vorschlag.** `include=annotations` auf dem Messages-Endpunkt. Annotations-Routen unter
`/projects/{projectId}/…` ziehen.

### C3. Weitere Doku-/Schema-Drift ⚠

`GET /sessions` hat im Schema keinen `status`-Filter, die Doku nennt einen. Solche Abweichungen
kosten Integratoren je einen Fehlversuch. Wenn das Schema generiert ist, sollte die Doku aus dem
Schema kommen, nicht daneben leben.

---

## D — Function-Runtime: Developer Experience

Jeder Punkt hier hat uns Zeit gekostet und steht heute als Warnung in unserem eigenen
README bzw. als Kommentar im Code. Das ist die Liste, die ein neuer Integrator sonst selbst
durchleidet.

| # | Beobachtung | Vorschlag |
|---|---|---|
| D1 | Der Invoke-Proxy verwirft Query-Strings. Alle Parameter müssen in den Pfad — unsere Route heißt deshalb `/transcript/{afterTimestamp}` statt `?after=`. | Query-Strings durchreichen. |
| D2 | Der Proxy liefert JSON-Bodies nicht geparst; der Client muss `Content-Type: text/plain` senden, sonst kommt der Body nicht an. Trifft jeden Unity-Integrator, weil `UnityWebRequest` `application/json` setzt. | `application/json` korrekt parsen und in `event.body` legen. |
| D3 | Env-Vars mit Präfix `CORTEX_` werden still entfernt. Eine gesetzte Variable liest sich als `undefined`. | Beim Speichern ablehnen, mit Begründung. |
| D4 | Event-Handler-Konvention unklar: Die Doku beschreibt `export const eventScopes` + `eventHandler`, unser Code nutzt `exports.onGatheringMemberLeft`. Im Function-API gibt es kein Feld für Event-Scopes, die Runtime introspektiert also. Ob unser Handler überhaupt feuert, wissen wir nicht. | Eine Konvention festlegen, dokumentieren — und im Dashboard anzeigen, welche Events eine deployte Function tatsächlich abonniert hat. |
| D5 | Kein lokaler Emulator. Wir haben für die Tests einen In-Memory-Mock der Cortex-REST-API nachgebaut. | `cortex functions dev` mit lokalem Invoke und Event-Injection. |
| D6 | Keine Logs über die API (nur `runtime-status`). Für Event-Handler, die man nicht per HTTP triggern kann, ist `console.log` sonst wertlos. | Log-Endpunkt bzw. Log-Ansicht pro Function und Invocation. |

---

## E — Identität: das fehlende Client-Credential

### E1. Kein participant-gescoptes Token (größter Hebel)

**Beobachtung.** Cortex kennt zwei Credentials: Dashboard-JWT und projekt-/tenant-weiter API-Key.
Beide sind Backend-Credentials. Für „dieser eine Spieler darf diese eine Sache" gibt es nichts.

**Auswirkung.** Wir haben in der Function ein eigenes Token-System gebaut: HMAC-signierte
Player-Tokens, eigenes Secret (`PLAYER_TOKEN_SECRET`), eigene Ablaufprüfung, eigener
`timingSafeEqual`-Vergleich. Rund 40 Zeilen Sicherheitscode, die jeder Cortex-Integrator neu
schreibt — und irgendwann schreibt sie jemand falsch.

**Vorschlag.** `POST /projects/{id}/participants/{id}/token` → kurzlebiges JWT mit `participantId`,
ausgestellt vom Backend, gehalten vom Client. Damit:

- kann der Client einen engen, für ihn freigegebenen Endpunkt-Satz direkt aufrufen,
- kann er **SSE mit Filter auf eigene Ressourcen** abonnieren → löst B1,
- entfällt der HMAC-Eigenbau in jeder Integration → löst E1,
- bekommt A1 einen natürlichen Ort für Capabilities.

Ein Feature, drei Probleme. Das ist der erste Kandidat.

### E2. API-Key als Query-Parameter

Die Doku nennt `?apiKey=ots_live_…` als unterstützte Variante. Solche Keys landen in Access-Logs,
Proxies und Browser-Historien. Sollte deprecated werden, mindestens aber nicht mehr dokumentiert.

---

## F — Kleinere Beobachtungen

- **F1.** Die Owner-Regel beim Beenden eines Gatherings hängt an `actorParticipantId` als
  Query-Parameter. Unser Code trägt den Kommentar, dass Deployments ohne diese Prüfung das Feld
  ignorieren. Ob die Regel serverseitig verbindlich ist, sollte eindeutig sein — sonst kann ein
  Client fremde Lobbies beenden.
- **F2.** Kein dokumentierter Rate-Limit-Contract. Wir haben client-seitig ein `RateLimitCooldown`
  gebaut und die Werte geraten (1 s / 3 s). Ein `429` mit `Retry-After` plus dokumentierte Budgets
  wären ehrlicher.
- **F3.** `GET /gatherings` liefert Member nur mit `includeMembers=true`, und `memberCount` zählt
  anders als die nach `status !== 'left'` gefilterte Liste. Die Unterscheidung mussten wir selbst
  herausfinden.

---

## Priorisierung

| Rang | Maßnahme | Löst | Aufwand (geschätzt) |
|---|---|---|---|
| 1 | Participant-gescoptes Token | E1, B1, Grundlage für A1 | mittel |
| 2 | Token-Capabilities aus aktiven Sanctions | A1 | mittel |
| 3 | `since` + `include=annotations` + ETag auf Messages | C1, C2, B4 | klein |
| 4 | Sanctions-Filter bzw. `GET /gatherings/{id}/sanctions` | A2 | klein |
| 5 | Push in den ODIN-Room über den Bot | B3, B4 | groß |
| 6 | Eskalations-Policy im Moderations-Plugin | A4 | mittel |
| 7 | Function-DX: D1, D2, D3, D4 | D | klein bis mittel |
| 8 | Scopes für Sanctions, Doku-Drift, `?apiKey` | A3, C3, E2 | klein |

Rang 3 und 4 sind die schnellen Erfolge: kleine Änderungen, sofort messbar in Traffic und Latenz,
kein Bruch bestehender Integrationen.

---

## Offene Fragen für die Plan-Session

1. **Was legt der Profanity-Filter konkret an?** Setzt er `participantId`, `sessionId` und vor allem
   `endAt`? Ohne `endAt` ist „Mute für X Minuten" nicht modellierbar und A4 wird Voraussetzung statt
   Kür. Prüfbar mit einem flagged Test-Sanction über `GET /sanctions?activeOnly=true`.
2. **Kann ODIN Voice serverseitig einen Peer am Publishen hindern?** Davon hängt ab, ob A1 in Cortex
   lösbar ist oder ein Voice-Thema wird.
3. **Kann der Bot in den Room senden**, oder ist er als reiner Subscriber gebaut? Bestimmt den
   Aufwand von B3.
4. **Stimmt die Schema-Lesart?** Die mit ⚠ markierten Punkte (C1, C3, A3) gegen die laufende API mit
   einem echten Key gegenprüfen, bevor Tickets daraus werden.
5. **Rolle des Samples:** Soll Boss Room den heutigen Stand zeigen (Polling, Client-Mute) und später
   nachziehen — oder warten wir mit den Moderations-Features im Sample, bis Cortex liefert, und
   bauen es dann als Referenzimplementierung? Das entscheidet, was in diesem Repo als Nächstes
   passiert.
