# Boss Room backend (ODIN Cortex function)

`bossroom-backend.js` replaces the Unity Gaming Services used by Boss Room:

| Unity service              | Replacement                                                    |
|----------------------------|----------------------------------------------------------------|
| Authentication (anonymous) | `POST /login` with a device id and profile, returns a signed player token |
| Multiplayer Services (sessions/lobby) | Cortex gatherings of type `lobby` (`/lobbies/...`)   |
| Relay                      | not needed, traffic runs over ODIN sockets in the lobby's ODIN room |
| Vivox (not used by Boss Room) | ODIN voice in the same room, plus Cortex transcription     |

## Endpoints

All requests except `/login` need the `X-Player-Token` header. Bodies are JSON, but the client sends them as
`text/plain` because the Cortex invoke proxy does not forward parsed JSON bodies. Query strings are dropped by
the proxy, so all parameters are part of the path.

| Method | Path | Description |
|--------|------|-------------|
| POST | `/login` | `{deviceId, profile, displayName}` → `{playerId, displayName, playerToken, expiresAt}` |
| GET  | `/lobbies` | Open, listed lobbies with free slots |
| POST | `/lobbies` | `{name, isPrivate, maxMembers}`; the caller becomes the owner |
| POST | `/lobbies/quickjoin` | Joins the newest open lobby, 404 if none |
| POST | `/lobbies/code/{joinCode}/join` | Join by code (also for private lobbies) |
| GET  | `/lobbies/{id}` | Lobby with members (members only) |
| POST | `/lobbies/{id}/join` | Join a listed lobby |
| POST | `/lobbies/{id}/leave` | Leave; the lobby ends when the owner leaves |
| POST | `/lobbies/{id}/kick` | `{playerId}`, owner only |
| POST | `/lobbies/{id}/token` | ODIN room token for the lobby's room (members only) |
| POST | `/lobbies/{id}/start` | Owner only; starts the transcription session if enabled |
| POST | `/lobbies/{id}/end` | Owner only |
| GET  | `/lobbies/{id}/transcript[/{afterTimestamp}]` | Transcribed messages with profanity flags |

The event handler `onGatheringMemberLeft` ends a lobby when its owner is removed.

## Deploy

1. In the ODIN Cortex dashboard, open your project and activate serverless functions.
2. Make sure the project's app settings can issue ODIN tokens (access key or rooms token provider). The Cortex
   bot and the game must use the same ODIN app.
3. Create an API key scoped to the project with the scopes `sessions`, `messages` and `plugins`.
4. Create a function:
   * slug: `bossroom-backend`, runtime `nodejs20`, auth mode `public`
   * code: the contents of `bossroom-backend.js`
   * env vars:
     * `BOSSROOM_API_URL` = `https://cortex.odin.4players.io`
     * `BOSSROOM_API_KEY` = the key from step 3
     * `PLAYER_TOKEN_SECRET` = a random string with at least 32 characters
     * optional `TRANSCRIPTION` = `true`, `GAME_ID`, `PLAYER_TOKEN_TTL`

   > Names starting with `CORTEX_` are reserved: Cortex injects `CORTEX_API_URL` and
   > `CORTEX_PROJECT_SECRET` itself and the runtime removes them from the environment before the
   > function runs, so a variable of that name would always read as undefined.
5. Publish and deploy the function.

   > Do **not** add an npm dependency named `crypto`. The runtime resolves built-in modules such as `crypto`
   > before project dependencies, and the npm package of that name is a deprecated placeholder. The function
   > binds it as `nodeCrypto`, because `crypto` is also the name of the global WebCrypto object.
6. For moderation flags, enable the profanity filter plugin in the project.
7. In Unity, set **Backend Url** in `Assets/Resources/OdinSampleConfig.asset` to
   `https://cortex.odin.4players.io/invoke/<projectId>/bossroom-backend`.
8. Open that URL in a browser: the function answers with its name, the configured game and its
   routes. That confirms deployment, environment and project id in one request.

## Local development without a backend

Leave **Backend Url** empty and set **Development Access Key** instead (generate one with
*Tools > 4Players ODIN > Setup Netcode Transport and Voice* or `OdinClient.CreateAccessKey()`).
Lobbies are then only reachable by join code, and tokens are created on the client. Never ship an access key.

## Tests

```bash
node --test Backend/cortex-function/test
```

The tests run the handler against an in-memory mock of the Cortex REST API.
