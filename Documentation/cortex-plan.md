# ODIN Cortex — Neubewertung der Findings und Umsetzungsplan

Stand: 2026-09-21. Grundlage: `Documentation/cortex-findings.md`, gegengeprüft am Quellcode von
Cortex (`odin-transcription-service`, `main` @ `793cf0c`), am ODIN Core SDK 2.2.6 (`odin.h`, README,
Protokoll-Strings der Binary), am Unity-SDK (`io.fourplayers.odin` @ `b38fda1`, Core 2.2.6), am
NGO-Transport (`io.fourplayers.odin.netcode` 2.0.0-preview.1) und an `@4players/odin-nodejs` 2.0.0
(das SDK, das der Cortex-Bot benutzt).

Nicht geprüft wurde die laufende Produktiv-API. Drei Fixes liegen erst seit dem 18.09. auf `main` —
ob sie deployt sind, ist offen (siehe Teil 1).

---

## Teil 1 — Neubewertung der Findings

| # | Urteil | Was der Code tatsächlich zeigt |
|---|---|---|
| A1 | **bestätigt, Lösungsvorschlag (a) trägt aber nicht** | `docs/Features/Sanctions.md:33` sagt es selbst: Cortex setzt nichts durch. Aber: ODIN-Tokens kennen keine Publish-Capability. `TokenOptions` in `@4players/odin-tokens` 2.2.0 = `customer, audience, subject, address, tags, upstream, lifetime, internal`. Ein Token wird beim Join geprüft, danach nicht mehr. „Capabilities im Token" ist also kein Cortex-Feature, sondern bräuchte ODIN-Server-Support. Machbarer Weg: siehe Teil 3, Baustein 4. |
| A2 | **bestätigt, schlimmer als beschrieben** | Die Tabelle `sanctions` hat **keine `projectId`-Spalte**. Der Projektfilter läuft per `INNER JOIN participants` — Sanctions, die nur eine `externalUserId` haben, verschwinden aus jeder Projektliste. `total` ist `sanctions.length` der Seite, nicht die Gesamtzahl. Participant-Infos werden per N+1 nachgeladen. |
| A3 | **beantwortet** | Sanctions sind nicht ungeschützt: der Controller verlangt den Scope `sessions` (`sanctions.controller.ts:51`). Es fehlt aber ein eigener Scope und die Lese/Schreib-Trennung. Doku-Lücke + Least-Privilege-Lücke. |
| A4 | **bestätigt + echter Bug** | `AutoSanctionPlugin.countRecentFlaggedAnnotations()` zählt geflaggte Annotationen **tenant-weit**, nicht pro Spieler. Drei Flüche von drei verschiedenen Spielern → der dritte wird gemutet. Außerdem lädt die Methode alle Annotationen des Zeitfensters in den Speicher. |
| B1 | **bestätigt + Skalierungsproblem** | Auth wie beschrieben. Zusätzlich: `EventStreamService` abonniert **einen Redis-Kanal pro Tenant** und filtert pro Subscriber im Prozess → Kosten O(Events × Verbindungen). `Last-Event-ID` wird angenommen, aber nirgends ausgewertet (kein Replay). Für Dashboard-Nutzer ok, für Spiel-Clients nicht. |
| B2 | bestätigt, **kein Mangel** | Request/Response ist für Functions das richtige Modell. Streams gehören nicht in die Function-Runtime. |
| B3 | **bestätigt, Aufwand „groß" → „mittel"** | Der Bot ist kein reiner Subscriber: er sendet bereits Audio (Ansagen). `@4players/odin-nodejs` 2.0.0 hat `room.sendMessage(payload, peerIds?)` und `room.sendRpc(json)`. Unity: `Room.OnMessageReceived(peerId, byte[])` ist frei; der NGO-Transport nutzt ausschließlich Sockets (Labels) und ignoriert Peers ohne Transport-UserData — der Bot stört ihn nicht. |
| B4 | bestätigt | Folge aus B1–B3 und C1. |
| C1 | **bestätigt, keine Doku-Drift** | `MessagesController.getMessages` hat keinerlei Query-Parameter, liefert immer alles und prüft pro Aufruf zusätzlich Debug-Audio für alle Message-IDs. Session wird gegen Tenant geprüft, nicht gegen die `projectId` aus dem Pfad. |
| C2 | **bestätigt, Sicherheitslücke verifiziert** | `POST /plugins/annotations/messages/batch` ruft `findByMessageIds(ids)` **ohne Tenant-Prüfung**. Jeder Key mit Scope `plugins` liest Annotationen fremder Tenants, wenn er Message-UUIDs kennt. Gleiches Muster: `GET sanctions/participant/:id` (kein Tenant-Check), `GET sanctions/user/:id` (tenant- statt projektweit). |
| C3 | bestätigt | `GET /sessions` hat weder `status`-Filter noch Pagination — die Liste ist unbegrenzt. |
| D1, D2 | **behoben** (`e350c35`, 18.09.) | Query-Strings und Bodies werden durchgereicht. Deploy-Stand prüfen, danach Workarounds im Sample entfernen. |
| D3 | **behoben** (`8190e10`, 18.09.) | `CORTEX_`-Variablen werden beim Speichern abgelehnt. |
| D4 | **beantwortet** | `exports.on<Event>` **ist** die kanonische Konvention (`libs/events/event-handlers.ts`, Abos werden beim Publish aus den Handler-Namen abgeleitet). `exports.onGatheringMemberLeft` ist korrekt und feuert — sofern die Function nach dem Hinzufügen neu *published* wurde. Offen bleibt: abgeleitete Abos sichtbar machen. |
| D5, D6 | bestätigt | Kein Emulator, keine Log-Route (Routenliste des `FunctionsController` geprüft). |
| E1 | bestätigt | Größter Hebel — aber als **asymmetrisch signiertes** Token, siehe Baustein 2. |
| E2 | bestätigt | `api-key.strategy.ts:64` liest `req.query['apiKey']`. Existiert vermutlich wegen Browser-`EventSource` (kann keine Header setzen). Wird mit dem Participant-Token überflüssig. |
| F1 | **behoben** (`d04c9ca`, 18.09.) | Ownership wird serverseitig erzwungen. |
| F2 | bestätigt | Kein Throttler in der API. |
| F3 | nicht geprüft | — |

### Zusätzliche Funde (nicht in der Liste)

1. **Sanction-Events aus der API tragen keine `projectId`.** `SanctionsService` publiziert
   `sanction.created/updated/revoked` ohne `projectId`; der projekt-gescopte SSE-Filter
   (`event.projectId !== options.projectId`) verwirft sie. Nur das Plugin setzt die `projectId`.
2. **Falscher Kommentar im Bot:** `setPeerAudible` ist als No-Op markiert, weil „die v2-Infrastruktur
   Channel-Masks nicht unterstützt" (Server: `invalid type: map, expected a sequence`). Das ist ein
   **Serialisierungsfehler im Node-SDK**, kein Infrastruktur-Mangel: `normalizeChannelMasks()` baut
   ein Objekt, der Server erwartet eine Liste von `[peerId, mask]`-Paaren mit der Maske als nackter
   Zahl. Das Unity-SDK macht es richtig (`Room.cs:1230ff`, inkl. Kommentar zum Parser).
3. **Unity-SDK `Room.SendMessage(string)`** sendet `{ "message": … }` statt
   `{ "SendMessage": { "message": [bytes], "peer_ids": […] } }` — sehr wahrscheinlich defekt.
   Für uns unkritisch (der Client empfängt nur), sollte aber ans SDK-Team.
4. **Auto-Sanction-Dauer:** Der Code behandelt ein fehlendes `sanctionDurationMinutes` als
   *permanent*, das Settings-Schema nennt 60 als Default. Ob der Default in die Instanz-Settings
   materialisiert wird, ist zu prüfen — sonst entstehen unbefristete Mutes.
5. **Heißer Pfad der API-Key-Auth:** jeder Request = 1 DB-Lookup + 1 DB-Write (`updateLastUsed`),
   bei `DB_POOL_MAX=4`. Für alles, was hochfrequent aufgerufen wird, ist das die erste Engstelle.

---

## Teil 2 — Antworten auf die offenen Fragen

**1. Was legt der Filter an?** `participantId` ✔, `externalUserId` ✔, `endAt` ✔ (wenn
`sanctionDurationMinutes` gesetzt, sonst permanent — siehe Zusatzfund 4), `sessionId` ✘,
`projectId` ✘ (Spalte existiert nicht). `type` default `mute`, Schwelle default 3 in 60 min — aber
tenant-weit gezählt (Bug A4). Dedupe nur über „hat schon aktiven Sanction dieses Typs".

**2. Kann ODIN Voice serverseitig das Publishen verhindern?** Über das Client-Protokoll: nein. Die
Core-SDK-Binary kennt genau vier Client-Calls: `Ping`, `ChangeSelf`, `SetChannelMasks`,
`SendMessage`. Kein Kick, kein Mute. *Aber* der Server kennt `Left { reason: "peer_kicked" }` — ein
Kick-Pfad existiert also serverseitig, nur nicht im SDK. Das ist eine Anfrage ans Voice-Team
(Admin-API). Wichtiger: es gibt einen Weg, der **heute schon dicht ist** — `SetChannelMasks` pro
Peer. Setzt ein Empfänger die Maske für Peer X auf 0, leitet der Server X' Audio nicht mehr an ihn
weiter. Das wird auf der Seite der *Empfänger* durchgesetzt; der gepatchte Client des Störers kann
es nicht umgehen. Im Core SDK vorhanden, im Unity-SDK vorhanden (`Room.SetChannelMasks`,
per-Peer-Overrides), im Node-SDK vorhanden aber defekt (Zusatzfund 2).

**3. Kann der Bot senden?** Ja, ohne SDK-Änderung: `room.sendMessage(payload, peerIds?)`, gezielt
oder Broadcast, reliable und geordnet über den Signalkanal, unabhängig von Masken/Proximity.
`PeerTrackingService` kennt bereits die Zuordnung peerId ↔ userId ↔ participantId.

**4. Schema-Lesart:** C1, C3 stimmen (Code, nicht nur Schema). A3: Scope `sessions`.

**5. Rolle des Samples — Empfehlung:** nicht warten. Nach Phase 1 stellt Boss Room das Polling auf
den Cursor-Endpunkt um (kleine Änderung, sofort −95 % Traffic). Nach Phase 3 wird es zur
Referenzimplementierung für Room-Push + Empfänger-seitiges Mute. Das Client-Mute von heute bleibt
bis dahin drin, als solches gekennzeichnet.

---

## Teil 3 — Zielarchitektur

Leitlinie für die Skalierung: **Der günstigste Client-Endpunkt ist der, den es nicht gibt.** Alles,
was im Room passiert, geht über den Room (ODIN trägt den Fan-out). Ein eigener Client-Endpunkt wird
nur für das gebaut, was *außerhalb* eines Rooms stattfindet — und dann als eigener, zustandsloser,
horizontal skalierender Dienst, nicht als Route in der REST-API.

```
Game-Backend / Function ──(API-Key | Project-Secret)──► Cortex API ──► Postgres
        │  mintet                                          │
        ▼                                                  ▼ Domain-Events (Redis)
  Participant-Token (EdDSA-JWT, ≤15 min)          ┌────────┴─────────┐
        │                                         ▼                  ▼
        ├──► /client/v1/* (REST, Redis-first)   Room-Push-Router   Realtime-Gateway (SSE)
        ├──► Realtime-Gateway (außerhalb Room)    │ bot:out:{sid}     ▲ rt:{project}:{topic}
        └──► Function (authMode: participant)     ▼                  │
                                              Bot-Worker ──sendMessage──► ODIN Room ──► Clients
```

### Baustein 1 — Korrektheit und Mandantentrennung (Voraussetzung für alles)

- `sanctions.projectId` (NOT NULL) + `gatheringId` (nullable) einführen, Backfill über
  `participants`/`sessions`. Indizes: `(project_id, external_user_id) WHERE revoked_at IS NULL`,
  `(project_id, session_id)`, `(project_id, gathering_id)`.
- Sanction-Erzeugung in eine gemeinsame Lib (`libs/domain`) ziehen; API und Plugin nutzen dieselbe
  Funktion → immer `projectId`, `sessionId`, identisches Event-Format.
- IDORs schließen: Annotations-Batch nach `/projects/{projectId}/…` verschieben und per Join auf
  `sessions.tenantId/projectId` filtern; `sanctions/participant/:id` und `sanctions/user/:id`
  projekt-scopen; `messages` zusätzlich gegen `projectId` prüfen.
- Auto-Sanction: pro Participant zählen — nicht per SQL-Scan, sondern als Redis-Sliding-Window
  (`ZADD flags:{project}:{participant} ts id` + `ZREMRANGEBYSCORE` + `ZCARD`). O(log n), kein
  DB-Zugriff im Event-Pfad.

### Baustein 2 — Participant-Token (E1)

`POST /projects/{id}/participants/token` (Scope `participants.token`), Body
`{ externalUserId, displayName?, gatheringId?, roomId?, ttlSeconds? (≤900) }`. Upsert des
Participants, Antwort `{ token, expiresAt, odinToken? }` — optional gleich das ODIN-Room-Token
mitliefern (ein Roundtrip; hier sitzt das Join-Gate aus Baustein 4).

- **EdDSA (Ed25519), nicht HMAC.** Claims: `iss, sub=participantId, pid, tid, ext, gth?, rid?, scp[],
  ep, exp`. Öffentlicher Schlüssel per JWKS (`/.well-known/jwks.json`, `kid`-Rotation). Damit
  verifiziert *jeder* Dienst — Gateway, Invoke-Worker, Function-Runtime — **ohne DB- oder
  Redis-Zugriff und ohne Geheimnis**. Das ist die Eigenschaft, die den Client-Pfad skalierbar macht.
- **Widerruf ohne Token-State:** `tokenEpoch` pro Projekt (Redis, 30 s lokal gecacht), Claim `ep`
  muss ≥ Epoch sein. Einzelne Spieler sperrt das Join-Gate, nicht die Token-Liste.
- **Invoke-Worker:** neuer `authMode: 'participant'`. Der Worker verifiziert und reicht
  `event.auth = { participantId, externalUserId, gatheringId, … }` an die Function. Damit entfallen
  die ~40 Zeilen HMAC-Eigenbau in `bossroom-backend.js` — und in jeder künftigen Integration.
- `?apiKey=` als deprecated markieren, aus der Doku nehmen, nach einer Frist entfernen.

### Baustein 3 — Read-Pfad, der Polling aushält (C1, C2, A2, F2)

- `messages.seq`: pro Session monoton (`bigint`, vergeben beim Insert). Cursor = `seq` — lückenlos,
  keine Timestamp-Kollisionen. Index `(session_id, seq)`.
- `GET …/sessions/{id}/messages?after=<seq>&limit=<≤200>&include=annotations,debugAudio`. Ohne
  `include` kein Debug-Audio-Check, keine Annotationen. Keyset-Pagination, Antwort mit `nextCursor`.
- **ETag aus Redis, nicht aus Postgres:** Bot-Worker setzt beim Insert `session:{id}:seq`. Ein Poll
  mit `If-None-Match: "<seq>"` wird mit einem einzigen Redis-`GET` als 304 beantwortet. Der
  unveränderte Poll — der Normalfall — kostet keine DB-Verbindung.
- Sanctions: Filter `participantIds[]`, `sessionId`, `gatheringId`, plus
  `GET /gatherings/{id}/sanctions`. Aktive Sanctions pro Spieler als Redis-Cache
  (`sanctions:active:{project}:{ext}`), invalidiert durch die Sanction-Events.
- `GET /sessions`: `status`, `limit`, Cursor.
- Scopes `sanctions` und `sanctions.read`; `sessions` deckt Sanctions übergangsweise weiter ab.
- **Rate-Limits** als Redis-Token-Bucket pro Key bzw. pro Participant, `429` + `Retry-After`,
  Budgets dokumentiert und als `RateLimit-*`-Header ausgegeben.
- API-Key-Auth: Key-Lookup 60 s cachen, `updateLastUsed` auf 1×/min pro Key drosseln.
- `/client/v1/…`: schmale, mit Participant-Token erreichbare Spiegelrouten (eigene Messages-Sicht,
  eigene Sanctions, eigenes Gathering). Autorisierung aus den Claims + Redis-gecachter
  Mitgliedschaft, nie per DB-Join im heißen Pfad.

### Baustein 4 — Durchsetzung in drei Schichten (A1, A4)

1. **Join-Gate (nur Cortex).** Token-Ausgabe prüft aktive Sanctions: `temp_ban`/`perm_ban` → `403`
   mit Sanction im Body; `mute`/`listen_only` → Token mit Tag `cortex:muted`. ODIN-Tokens kurzlebig
   (`lifetime` 300 s) — sie werden nur für den Join gebraucht. Einschränkung: `tags`/`lifetime` gehen
   heute nur beim Provider `access_key`; der `rooms`-Provider (Payment `create-token`) nimmt nur
   `roomId, userId` → Anfrage ans Payment-Team, Optionen durchzureichen.
2. **Empfänger-seitiges Mute im Room (Cortex + Client-SDK, geht heute).** Bot broadcastet
   `cortex.sanction`; jeder Client setzt `SetChannelMasks` für den Peer auf 0 → der ODIN-Server
   liefert dessen Audio nicht mehr aus. Neue Peers erhalten die aktive Mute-Liste vom Bot beim
   `PeerJoined` (bzw. lesen das Tag). Der Bot maskiert den Peer ebenfalls → keine Transkription,
   keine Folge-Flags, keine STT-Kosten. Voraussetzung: Node-SDK-Fix (Zusatzfund 2).
   Restrisiko: ein Empfänger mit gepatchtem Client hört weiter zu — das schadet nur ihm selbst.
3. **Harte Durchsetzung (Voice-Team).** Admin-API `kick(room, peer)` und idealerweise ein
   Token-Claim/Tag, den der SFU als „darf nicht senden" auswertet. Mit Kick + Schicht 1 ist auch ein
   Ban während laufender Session dicht.

**Eskalationsleiter** als Plugin-Setting: `ladder: [{ type, durationMinutes }]`,
`windowMinutes`, `lookbackDays`. Stufe = Anzahl früherer Auto-Sanctions im Lookback. `endAt` wird
immer gesetzt (auch `warn`, dann als Anzeigedauer). Permanent nur explizit.

### Baustein 5 — Room-Push über den Bot (B3, B4)

- **Routing:** Der Bot-Worker abonniert beim Join `bot:out:{sessionId}` (exakter Kanal, Unsubscribe
  beim Leave). Ein Room-Push-Router (im Plugin-Worker oder als eigenes Modul) bildet Domain-Events
  auf Sessions ab — über `sessionId` im Event oder das Redis-Set `live:participant:{id}`, das
  `PeerTrackingService` pflegt — und publiziert gezielt. Kein Worker filtert Tenant-Traffic.
- **Events, pro Projekt konfigurierbar:** `sanction.*` (gezielt + Enforcement-Broadcast),
  Live-Transkript (siehe unten, **ersetzt das Transkript-Polling vollständig**),
  geflaggte Annotationen, `gathering.*`.
- **Live-Transkript, der Sonderfall mit dem kürzesten Pfad:** Das Transkript entsteht im
  Bot-Prozess, der die Room-Verbindung hält. Der Bot sendet es direkt nach der STT-Antwort in den
  Room, ohne Redis, ohne Router, parallel zum Persistieren. Latenz = Segmentlänge + STT-Zeit
  (heute: Flush nach 1,5 s Stille, max. 30 s, dann Whisper → grob 2–4 s nach Satzende).
  - Frame: `transcript { segmentId, peerId, participantId, text, lang, interim, seq, ts }`.
    `interim: true` ist von Anfang an im Protokoll, auch wenn Whisper nur Finals liefert: sobald
    ein Streaming-STT-Provider (Deepgram, AssemblyAI, OpenAI Realtime) im Bot steckt, aktualisieren
    Clients Untertitel in place, ohne Protokolländerung.
  - `peerId` statt `senderName`: der Client kennt die Peers und hängt den Text direkt an den
    richtigen Spieler.
  - Übersetzung kommt als Plugin über den Event-Bus zurück (`transcript.translation
    { segmentId, lang, text }`) und wird **gezielt per `peer_ids`** in der Sprache des jeweiligen
    Spielers gesendet (bevorzugte Sprache aus User-Data oder Participant-Datensatz). In
    E2EE-Rooms läuft alles durch den Cipher des Bots.
  - Der Client braucht dafür nichts von Cortex: kein Token, kein Endpunkt. Room + Listener.
  - Projekt-Setting `transcriptPush: off | all | own` — Text ist kopierbar, das schaltet der
    Kunde bewusst ein.
- **Eigene Nachrichten:** `POST /projects/{id}/sessions/{sessionId}/room-messages`
  `{ targetExternalUserIds?, type, data }`, Scope `rooms.push`, in Functions als
  `ctx.cortex.rooms.send()`. Limit pro Room (z. B. 20 msg/s, 8 KB).
- **Koexistenz mit App-Messages (kein Kanal wird belegt):** `SendMessage` ist unstrukturiert, der
  Namensraum ist deshalb der *Absender*. `MessageReceived` liefert die Sender-`peer_id`; Cortex
  sendet ausschließlich vom Bot-Peer. Regel für Clients: Nachrichten vom Bot-Peer sind Cortex,
  alles andere gehört der App. Text-Chat zwischen Spielern bleibt unberührt.
- **Authentizität** ist dieselbe Prüfung: Der Bot trägt das Tag `cortex:bot` aus seinem
  *signierten* Token — Clients können es nicht fälschen, solange Cortex dieses Tag nie an Spieler
  ausgibt. (Nur mit `access_key`-Provider. Fallback: Ed25519-Signatur im Envelope mit demselben
  JWKS wie Baustein 2.)
- **Envelope („Cortex Room Protocol"), damit auch generische Parser Cortex-Frames erkennen:**
  Byte 0–1 Magic `0x43 0x58` („CX"), Byte 2 Version, danach MessagePack/JSON
  `{ "type": "sanction.created", "id": "…", "seq": n, "data": {…} }`. Veröffentlicht mit einer
  Referenz-Implementierung pro SDK (Unity: `CortexRoomListener`, filtert nach Absender, prüft
  Magic, feuert typisierte Events).
- **Sockets** wären das sauberere Multiplexing (`label` als Namensraum, wie beim NGO-Transport),
  sind aber heute nicht für den Bot nutzbar: `@4players/odin-nodejs` 2.0.0 exponiert keine
  Sockets, Sockets überleben keinen Reconnect, und ob ein Broadcast-Socket (`remote_peer_id = 0`)
  später beitretende Peers erreicht, ist nicht dokumentiert. Im Protokoll wird ein Socket-Label
  für Bulk-Fälle reserviert (Transkript-Replay nach Reconnect, Audio-Belege), nutzbar sobald das
  Node-SDK Sockets kann.
- **Option für später:** Da der Bot alle Broadcasts empfängt, kann App-Text-Chat in einem
  dokumentierten Format durch dieselbe Moderation laufen wie die Transkripte.
- **Lücken:** Nach Reconnect holt der Client per `after=<seq>` nach. Push ist der schnelle Weg,
  der Cursor-Endpunkt die Wahrheit.
- **Skalierung:** Cortex-Kosten = 1 `sendMessage` pro Event pro Room, unabhängig von der
  Spielerzahl. Der Fan-out liegt im ODIN-SFU. **Keine einzige neue Client-Verbindung zu Cortex.**
  Rechenbeispiel aus B4: 100 Lobbies × 8 Spieler = 400 req/s Polling heute → 0 req/s im Normalbetrieb.
- **Grenze:** funktioniert nur, solange ein Bot im Room ist (aktive Session). Für „Push ohne
  Transkription" braucht der Bot einen stummen Modus (Join ohne Decoder/STT) — klein, aber
  abrechnungsrelevant, also Produktentscheidung.

### Baustein 6 — Realtime-Gateway für alles außerhalb eines Rooms (B1)

Eigene App `apps/realtime-gateway`, getrennt von der REST-API deployt und skaliert.

- **Protokoll:** SSE (`GET /rt/v1/stream?topics=…`, `Authorization: Bearer <participant-token>`).
  SSE reicht (nur Server→Client), geht durch jeden Proxy, in Unity per
  `HttpClient` + `ResponseHeadersRead`. WebGL: `fetch`-Streaming statt `EventSource`.
- **Topic-Kanäle statt Tenant-Kanal:** `publishDomainEvent` publiziert zusätzlich auf
  `rt:{project}:gathering:{id}`, `rt:{project}:session:{id}`, `rt:{project}:participant:{id}`.
  Das Gateway abonniert einen Redis-Kanal nur, solange mindestens ein lokaler Client ihn hört
  (Refcount). Kosten: O(Events × tatsächliche Hörer dieses Topics) statt O(Events × alle
  Verbindungen des Tenants).
- **Autorisierung pro Topic aus den Claims** (`gth`, `rid`, `sub`) — kein DB-Zugriff beim Connect.
- **Zustandslos:** keine Sticky Sessions. Erster Frame ist ein Snapshot (aktive Sanctions,
  Gathering-Stand, aktueller `seq`), danach Deltas. Dadurch ist kein Replay-Log nötig; Lücken
  schließt der Client über die Cursor-Endpunkte. (`Last-Event-ID` entweder so implementieren oder
  aus dem Vertrag streichen — heute ist es ein leeres Versprechen.)
- **Schutz:** max. 2 Streams pro Participant, Cap pro Projekt, Heartbeat 25 s, langsame Konsumenten
  werden getrennt statt gepuffert, Token-Ablauf beendet den Stream, Client reconnectet mit Jitter.
- **Kapazität:** ein Node-Prozess trägt 10–20 k idle SSE-Verbindungen; Autoscaling nach
  Verbindungszahl, nicht CPU. Redis Pub/Sub skaliert eine Instanz weit; die Kanal-Namen sind so
  gewählt, dass ein späterer Wechsel auf Sharded Pub/Sub (`SPUBLISH`, Redis 7 Cluster) nur den
  `EventBusService` betrifft. Auf Cloud Run: 60-min-Request-Limit einplanen (Reconnect ist ohnehin
  Teil des Vertrags).
- **Lasttest als Abnahmekriterium:** 50 k gleichzeitige Verbindungen, 500 Events/s auf 5 k Topics,
  p99 Zustellung < 250 ms, kein Postgres-Zugriff im Steady State.

Die Dashboard-SSE-Routen bleiben, wie sie sind.

---

## Teil 4 — Phasenplan

| Phase | Inhalt | Löst | Aufwand | Abhängig von |
|---|---|---|---|---|
| **0** | Baustein 1 komplett; Zusatzfunde 1, 4; Kommentar `setPeerAudible` korrigieren; Deploy-Stand von D1–D3/F1 prüfen | A4-Bug, C2-Lücke, A2-Basis | 3–4 Tage | — **MR gestellt (2026-09-22):** [odin-transcription-service!10](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/10); enthält bereits die Sanction-Filter aus Phase 1. Offen aus Phase 0: `setPeerAudible`-Kommentar (kommt mit dem Node-SDK-Bump auf 2.0.1) |
| **1** | Baustein 3 (Cursor, `include`, ETag, Filter, Scopes, Rate-Limits, Key-Cache); SDK + OpenAPI + Vault nachziehen | C1, C2, C3, A2, A3, F2 | ~1 Woche | 0 — **gemerged und deployt (2026-09-22, Pipeline 68709):** [odin-transcription-service!11](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/11). Umfang: `messages.seq` (DB-vergeben, Redis-Spiegel), `after`/`limit`/`order`/`include` + `nextCursor`/`latestSeq`, 304 aus Redis, Session-Liste mit `status`/Cursor, Scopes `sanctions`/`sanctions.read`, Token-Bucket pro Key/Project-Secret mit `429`+`Retry-After`, Key-/Secret-Lookup 60 s gecacht, SDK 1.8.0 (`listMessages`, `listPaged`). Legacy-Form (ohne Query-Parameter) bleibt für Console/`getMessages()` erhalten. Die damals offenen Punkte sind mit Phase 2 erledigt: Sanction-Redis-Cache und `GET /gatherings/{id}/sanctions` sind in [!13](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/13), `?apiKey=` ist dort entfernt (nicht mehr nur deprecated), und SDK **1.8.0 liegt seit 2026-09-22 auf npm** (`latest`) |
| **2** | Baustein 2 (Participant-Token, JWKS, `authMode: participant`, `/client/v1`) | E1, E2 | 1–1,5 Wochen | 1 — **gemerged und deployt (2026-09-22, Pipeline 68717):** [odin-transcription-service!13](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/13). Umfang: EdDSA-Token (Ed25519, Claims `iss/sub/pid/tid/ext/gth?/rid?/scp[]/ep/iat/exp`, `kid` im JWS-Header, ≤ 15 min), Schlüssel aus `PARTICIPANT_TOKEN_PRIVATE_KEY` mit Rotation über `PARTICIPANT_TOKEN_PUBLIC_KEYS`, `GET /api/.well-known/jwks.json` (öffentlich, 300 s cachebar; **nicht** am Root — das Produktions-nginx reicht nur `/api/*` durch), `POST …/participants/token` (+ ODIN-Token bei `roomId`) und `…/token/revoke-all` über die Redis-Epoch (30 s Prozess-Cache), Scope `participants.token`, eigene Auth-Strategie + Guard (nicht im `CombinedAuthGuard`), `/client/v1/{me,sessions/:id/messages,sanctions,gatherings/:id,gatherings/:id/ready}` mit 404 statt 403, Rate-Limit pro Participant (60 Burst / 5 req/s), `authMode: 'participant'` im Invoke-Worker mit `event.auth` (Runtime **v4**), `GET …/gatherings/{id}/sanctions`, Redis-Cache `sanctions:active:{project}:{ext}`, `?apiKey=` entfernt, SDK 1.9.0 (`participants.issueToken()`, `CortexClient({ participantToken })` → `client.me`). Keine Migration nötig. Runtime `:v4` liegt auf Docker Hub, SDK 1.9.0 auf npm (`latest`). In Produktion verifiziert: JWKS antwortet `200 {"keys":[]}`, alle neuen Routen sind gemappt, Bestandsrouten unverändert. **Offen:** `PARTICIPANT_TOKEN_PRIVATE_KEY` ist noch nicht gesetzt — bis dahin liefert die Mint-Route `503` und das Feature ruht. Join-Gate (Ban → 403, Mute → Tag) bleibt markiert für Phase 3 |
| **3** | Baustein 5 + Baustein 4 Schichten 1–2 + Eskalationsleiter | A1, A4, B3, B4 | ~2 Wochen | 0, 2; Node-SDK-Fix — **MRs gestellt (2026-09-23), bewusst in zwei Teilen:** [odin-transcription-service!16](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/16) (Baustein 5) und [!17](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/17) (Baustein 4, gestapelt auf !16). Grund für den Schnitt: Runtime v5 pinnt SDK 1.10.0, das erst nach dem Merge von !16 per `publish:sdk` auf npm liegt — sonst scheitert `build:functions-runtime` und neue Aktivierungen würden auf ein nicht existierendes `:v5` gepinnt. **Reihenfolge:** !16 mergen → `publish:sdk` (1.10.0) → !17 mergen → `publish:sdk` (1.11.0). **!16:** Cortex Room Protocol (`CX` + Version + JSON `{type,id,seq,data}`, nur vom Bot-Peer, Tag `cortex:bot`), Bot abonniert exakt `bot:out:{sessionId}`, Room-Push-Router im Plugin-Worker über das Redis-Set `live:user:{project}:{ext}`, `cortex.transcript` direkt aus dem Bot (nach dem INSERT, weil nur der den Cursor-`seq` vergibt; `lang` als ISO 639-1), `cortex.sanction`-Hinweise, `POST …/sessions/{id}/room-messages` (Scope `rooms.push`, 8 KB → 413, 20 msg/s pro Room → 429, kein Bot → 409), Projekt-Settings `transcriptPush` (`off`/`all`/`own`) und `sanctionPush` (beide default aus, Migration 0029 nur `ADD COLUMN … DEFAULT`), SDK 1.10.0 (`project.rooms.send()`, `decodeCortexRoomFrame()`). **Nebenfund, mitgefixt:** `@4players/odin-tokens` braucht `ArrayBuffer#transfer`, das Node 20 (Produktions-Image) nicht hat — der `access_key`-Provider konnte in Produktion gar keine Tokens erzeugen; Polyfill in `libs/odin`. **!17:** Join-Gate (Ban → 403 mit Sanction, Mute → ODIN-Token-Tag `cortex:muted`, `lifetime` 300 s; beim `rooms`-Provider ohne Tag/Lifetime, `odinTokenRestrictions.applied=false`), empfänger-seitiges Mute (Broadcast `cortex.sanction {mute}`, `cortex.mutes`-Snapshot beim `PeerJoined`, Bot maskiert den Peer selbst, Ablauf per Timer), Eskalationsleiter (`ladder`, `windowMinutes`, `lookbackDays`; `endAt` immer; Zusatzfund 4 behoben: `null`/`0`/fehlend = 60 min, permanent nur explizit), Runtime **v5** (`ctx.cortex.rooms.send()`, als breaking markiert: SDK beta.11 → 1.10.0), SDK 1.11.0 (`CortexJoinRefusedError`). **Smoke-Test lokal gegen echten ODIN-v2-Room** (selbst erzeugter Dev-Access-Key, API/Bot/Plugin-Worker lokal): !16 22/22, !17 25/25 inkl. Kette Sprache → Whisper → Moderation → Auto-Sanction (warn → mute). **Nicht getestet:** gegen Produktion, ODIN-1.x-Rooms, `rooms`-Token-Provider (lokal kein gültiges Payment-Secret), Unity-Client (nur Node-Peers) |
| **4** | Baustein 6 (Realtime-Gateway) inkl. Lasttest | B1 | ~2 Wochen | 2 |
| **5** | Function-DX: Log-Endpunkt, abgeleitete Event-Abos in API/Console anzeigen, `cortex functions dev` | D4, D5, D6 | 1–2 Wochen | — (parallel) |

Phase 3 vor Phase 4, weil sie den größeren Nutzen für Spiele bringt und keine neue
Client-Infrastruktur braucht. Phase 4 nur vorziehen, wenn ein Kunde Push *vor* dem Room-Join braucht.

### Deploy-Randbedingungen (2026-09-22 geprüft)

- **Ingress:** vor `cortex.odin.4players.io` steht ein nginx, das **ausschließlich `/api/*`**
  durchreicht (`/`, `/metrics`, `/.well-known/*` → 403 von nginx, nicht von der App). Alles Neue
  muss deshalb unter `/api` liegen; der JWKS-Endpunkt wurde entsprechend auf
  `/api/.well-known/jwks.json` verschoben. Ein Root-Mount wäre ein Ops-Ticket, kein Code-Change.
- **Deploy ist all-or-nothing:** `deploy:fleet` aktualisiert auf `main` alle fünf Images in einem
  Job (API, Bot-, Webhook-, Plugin-, Functions-Invoke-Worker), gegated auf `migrate:database`.
  Es gibt kein Staging und keinen Canary.
- **Keine Migration** in Phase 2 (`drizzle-kit check` sauber, neue Werte sind `jsonb`/`text`),
  die Reihenfolge Migration → Deploy ist damit unkritisch.
- **Neue Pflicht-Variable:** `PARTICIPANT_TOKEN_PRIVATE_KEY` auf der API. Fehlt sie, bootet der
  Prozess normal, die Mint-Route liefert `503`, JWKS liefert `{"keys":[]}` — kein Absturz. Für
  `authMode: participant` braucht auch der Functions-Invoke-Worker den Schlüssel (oder nur den
  öffentlichen Teil über `PARTICIPANT_TOKEN_PUBLIC_KEYS`).
- **Redis:** der Functions-Invoke-Worker öffnet jetzt **eine** Redis-Verbindung (vorher keine) für
  die Token-Epoch. `REDIS_URL` ist laut Doku ohnehin für jeden Dienst Pflicht; ohne erreichbares
  Redis bootet er trotzdem und loggt alle 2 s einen Reconnect-Warn — funktional fallen nur die
  Epoch-Prüfungen offen aus.
- **Boot-Tests:** Plugin-Worker (neue `ActiveSanctionsCacheService`-Abhängigkeit über `DomainModule`)
  und Functions-Invoke-Worker (neue Token-Services) starten ohne DI-Fehler, auch ohne gesetzten
  Schlüssel.
- **Breaking für Clients:** `?apiKey=` ist entfernt (401 statt 200). In `b2b-console` nicht
  verwendet — die Console streamt SSE über `fetch` mit Header. Externe Integratoren, die es noch
  nutzen, müssen auf `X-API-Key` wechseln.

### Anfragen an andere Teams (früh stellen, blockieren Phase 3 nur teilweise)

| Team | Anfrage | Wofür | Stand |
|---|---|---|---|
| Node-SDK | `setChannelMasks` und `channelMasks` (Join): Masken als `[[peerId, mask], …]` serialisieren | Bot-Mute, Baustein 4.2 | **Erledigt:** `@4players/odin-nodejs@2.0.1` liegt auf npm (dist-tag `next`), Cortex pinnt es als `@4players/odin-nodejs-v2` ([!12](https://gitlab.4players.de/odin/integrations/odin-transcription-service/-/merge_requests/12)). In Phase 3 gegen das Live-Gateway verifiziert: der Bot maskiert gemutete Peers, deren Audio kommt nicht mehr an |
| Unity-SDK | `Room.SendMessage` korrigieren (sendete `{message}` statt des `SendMessage`-RPCs); `SendMessage(byte[], peerIds)` ergänzen; **eingehende `MessageReceived`-Events wurden gar nicht verarbeitet** (`ProcessJsonRpc` → NotImplemented), `OnMessageReceived` feuerte nie | Zusatzfund 3, Empfangspfad für Baustein 5 | **PR gestellt:** [odin-sdk-unity#2](https://github.com/4Players/odin-sdk-unity/pull/2), 5 Unit-Tests |
| Voice | Admin-API `kick(room, peer/user)`; Tag oder Claim „darf nicht senden" im SFU | Baustein 4.3 | offen |
| Payment | `create-token` um `tags`, `lifetime` erweitern | Join-Gate (`cortex:muted`, 300 s) und Bot-Tag `cortex:bot` beim `rooms`-Provider | offen — Cortex degradiert sauber: Token ohne Tag/Lifetime, `odinTokenRestrictions.applied=false`, Clients erkennen den Bot dann nur über seine User-ID; der 403 bei Bans wirkt unabhängig vom Provider |
| Node-SDK | Sockets exponieren; klären, ob ein Broadcast-Socket späte Peers erreicht | Bulk-Kanal in Baustein 5 | offen |

### Was sich im Boss-Room-Sample ändert

> **Stand 2026-09-23 — Sanktionsdurchsetzung umgesetzt (Phase 3, Schichten 1–2):**
> - **Backend-Function:** Raum-Token über `POST /participants/token`, also durch das Join-Gate. Ein Ban liefert `403 banned` mit lesbarer Meldung; Mutes bekommen den Tag `cortex:muted`. Die ODIN-User-ID ist jetzt die `externalUserId` statt der Participant-UUID; vorher legte der Bot pro Spieler einen zweiten Participant an. Der Key braucht zusätzlich die Scopes `gatherings` und `participants.token`.
> - **Unity:**
>   - `CortexRoomListener` findet den Bot-Peer, maskiert gemutete Peers per `SetListenChannelMaskForPeer(None)` und wertet `cortex:muted` beim Join aus.
>   - Warnung und Mute erscheinen als HUD-Hinweis, ein Ban verlässt das Spiel.
>   - Parser und Mute-Set liegen in `CortexRoomProtocol`, getestet in `CortexRoomProtocolTests`.
>   - SDK-Pin auf odin-sdk-unity#2 (`b9c25468`).
> - **Offen:** Transcript-Push statt Polling (`cortex.transcript`) sowie Phase 1/2 im Sample (Cursor, `/client/v1`, HMAC-Tokens ablösen).

- Nach Deploy-Check: `/transcript/{afterTimestamp}` → `?after=`, `Content-Type: text/plain`-Workaround raus.
- Nach Phase 1: `transcript()` nutzt `after=<seq>&include=annotations`, kein In-Memory-Filtern mehr.
- Nach Phase 2: HMAC-Player-Tokens und `PLAYER_TOKEN_SECRET` entfallen. Konkret:
  - Das Backend (oder die Cortex-Function) holt pro Spieler
    `POST /api/projects/{projectId}/participants/token` mit
    `{ externalUserId, displayName?, gatheringId?, roomId? }` (Key-Scope `participants.token`) und
    reicht `token` — und, wenn `roomId` gesetzt war, gleich `odinToken` — an den Client weiter.
    Der Participant wird dabei angelegt; das Sample braucht keine Cortex-UUIDs mehr.
  - Der Unity-Client spricht mit `Authorization: Bearer <token>` direkt:
    `GET /api/client/v1/me` (Identität + `expiresAt` für den Refresh),
    `GET /api/client/v1/sessions/{sessionId}/messages?after=<seq>&limit=200` mit
    `If-None-Match: "<seq>"` (ersetzt den bisherigen Backend-Proxy für das Transkript),
    `GET /api/client/v1/sanctions` (liefert `muted`/`banned` fertig abgeleitet, das eigene
    `RateLimitCooldown`-Raten entfällt),
    `GET /api/client/v1/gatherings/{gatheringId}` und
    `POST /api/client/v1/gatherings/{gatheringId}/ready` statt des Owner-/Member-Umwegs.
  - `bossroom-backend.js`: die ~40 Zeilen HMAC-Token-Code raus, Function auf
    `authMode: 'participant'` umstellen und `event.auth`
    (`{ participantId, externalUserId, projectId, gatheringId?, roomId?, scopes }`) lesen —
    setzt Runtime **v4** voraus.
  - Token laufen nach spätestens 15 min ab: vor `expiresAt` nachholen, ein `401` heißt
    "neues Token holen", nicht "nochmal mit demselben versuchen". Budget im Client:
    60 Burst / 5 req/s pro Spieler.
- Nach Phase 3 (setzt voraus: [odin-sdk-unity#2](https://github.com/4Players/odin-sdk-unity/pull/2),
  sonst feuert `OnMessageReceived` nie). Das ist dann die Referenzimplementierung (`CortexRoomListener`):
  - **Projekt-Settings:** `transcriptPush: "all"` (Untertitel für alle) oder `"own"`, `sanctionPush: true`.
    Für Bot-Tag und Mute-Tag Token-Provider `access_key` — mit `rooms` fehlen beide Tags.
  - **Push-Listener statt Polling:** `OnPeerJoined` merkt sich den Bot-Peer (Tag `cortex:bot`, Fallback
    User-ID `transcription-bot`). `OnMessageReceived`: nur Nachrichten vom Bot-Peer, Magic `0x43 0x58` +
    Version `1` prüfen, ab Byte 3 JSON `{ type, id, seq, data }`. `cortex.transcript` hängt `data.text`
    an den Spieler mit `data.peerId`; `interim: true` ersetzt die Zeile mit gleicher `segmentId`. Der
    Transkript-Polling-Loop entfällt — `GET /client/v1/sessions/{id}/messages?after=<data.seq>` nur noch
    nach einem Reconnect oder wenn das Frame-`seq` springt.
  - **Mute über `Room.SetChannelMasks`:** Set gemuteter Peer-IDs führen. `cortex.mutes` ersetzt es,
    `cortex.sanction` mit `mute: true` fügt `data.peerIds` hinzu, `mute: false` entfernt sie; jede Änderung
    als per-Peer-Override `SetChannelMasks(peerId → 0)` bzw. zurück auf alle Kanäle. Peers, die mit Tag
    `cortex:muted` joinen, sofort maskieren. Das heutige Client-Mute (lokal stummschalten) fällt weg —
    die Maske wird vom ODIN-Server durchgesetzt, ein gepatchter Client des Störers kommt nicht vorbei.
  - **Hinweise an den eigenen Spieler:** `cortex.sanction` ohne `mute` (z. B. `warn` mit `endAt` als
    Anzeigedauer) im UI zeigen; bei `temp_ban`/`perm_ban` den Room verlassen.
  - **Backend/Function:** `POST …/participants/token` liefert bei Ban `403` mit der Sanction im Body
    (SDK: `CortexJoinRefusedError`) — dem Spieler „gesperrt bis …“ anzeigen statt zu joinen. Eigene
    Nachrichten (z. B. Lobby-Countdown) per `ctx.cortex.rooms.send(sessionId, { type, data })` (Runtime v5)
    statt über einen Poll-Endpunkt.
  - **Auto-Sanction-Plugin:** Leiter z. B. `["warn:1", "mute:5", "mute:30", "temp_ban:1440"]`.
