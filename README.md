# Boss Room on 4Players ODIN

[![UnityVersion](https://img.shields.io/badge/Unity%20Version:-6000.6.1f1-57b9d3.svg?logo=unity&color=2196F3)](https://unity.com/releases/editor/archive)
[![NetcodeVersion](https://img.shields.io/badge/Netcode%20Version:-2.13.2-57b9d3.svg?logo=unity&color=2196F3)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/releases/tag/v2.13.2)
[![ODIN](https://img.shields.io/badge/ODIN-Sockets%20%2B%20Cortex-57b9d3.svg?color=8A2BE2)](https://www.4players.io/odin/)

This is Unity's [Boss Room](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop)
sample with **every Unity Gaming Service replaced by [4Players ODIN](https://www.4players.io/odin/)**.
The game is unchanged — same classes, same boss, same eight players — but nothing behind it is a Unity
service any more. Game traffic runs over ODIN sockets, lobbies are ODIN Cortex gatherings, sign-in and
tokens come from a Cortex serverless function, and voice chat rides along in the room that is already
open for the netcode.

It exists for two reasons: to try Boss Room on ODIN, and to show in one diff how much (or how little) it
takes to move a real multiplayer game onto ODIN.

> The original Boss Room documentation — architecture, the index of gameplay patterns, art notes — is
> kept in [README-BossRoom.md](README-BossRoom.md). Nothing about the gameplay changed.

<br>

## What was replaced

| Concern | Boss Room before | This fork |
|---|---|---|
| Transport | Unity Transport + Relay allocation | **ODIN Sockets** via `io.fourplayers.odin.netcode` |
| Lobby / sessions | `com.unity.services.multiplayer` (Sessions) | **ODIN Cortex gatherings** of type `lobby` |
| Sign-in | `com.unity.services.authentication`, anonymous | **Cortex function** `/login`, device id + local profile |
| Room tokens | Relay join codes | **Cortex function** `/lobbies/{id}/token` |
| Voice chat | none | **ODIN voice** in the same room: proximity + party channel |
| Transcription / moderation | none | **Cortex** transcription and the profanity plugin |
| Direct IP connections | `IPUIMediator`, IP popup | removed — there is nothing to forward a port to |
| Network simulator | `com.unity.multiplayer.tools` | removed, it only drives the Unity transport |

No Unity service package is left in [Packages/manifest.json](Packages/manifest.json), and the project
runs without a `cloudProjectId`.

<br>

## How a match comes together

```
Player ──1── POST /login ────────────────► Cortex function ──► Cortex participant
       ──2── POST /lobbies ─────────────► Cortex function ──► gathering (type: lobby)
       ──3── POST /lobbies/{id}/token ──► Cortex function ──► ODIN room token
       ──4── join ODIN room ────────────► ODIN
                                            ├── sockets  → Netcode for GameObjects traffic
                                            └── media    → proximity + party voice
```

One room join covers both. There is no relay to allocate, no port to forward and no public IP anywhere,
because every peer talks to the ODIN gateway. The host is simply the peer that joined the room with
`role: host` in its user data.

<br>

## Packages

| Package | Source | What it does |
|---|---|---|
| `io.fourplayers.odin` | [github.com/4Players/odin-sdk-unity](https://github.com/4Players/odin-sdk-unity), branch `feature/socket-inbound-info` | ODIN SDK. The branch adds socket info for inbound sockets, which a transport needs to tell peers and channels apart |
| `io.fourplayers.odin.netcode` | `gitlab.4players.de/odin/integrations/odin-transport-for-unity-ngo`, branch `feature/odin-sockets` | The Netcode transport and the voice components |

Both are pinned to a commit in the manifest, so everyone resolves the same code. Unity runs your own
`git` to fetch them, so git has to be on your PATH, and the GitLab one needs access to that repository.
To move to a newer version, replace the hash after the `#`.

<br>

## Getting it running

### 1. Clone

Boss Room keeps its scenes, prefabs and textures in Git LFS. Install it **before** cloning, otherwise
the assets arrive as text pointers:

```bash
git lfs install
```

Then clone this fork and open it with **Unity 6000.6.1f1**. The first import takes a while, and the
package manager will fetch the two ODIN packages from git while it does.

### 2. Create an ODIN project

Sign up at [console.4players.io](https://console.4players.io) and create a project. You need two things
from it:

* the **project id** — it is part of the function's invoke URL
* an **API key** scoped to the project, with the scope `sessions` (that covers gatherings and room
  tokens). Add `messages` and `plugins` as well if you want transcription and moderation.

Make sure the project can issue ODIN tokens, either through an access key or the rooms token provider.
The game and the Cortex bot have to use the same ODIN app.

### 3. Deploy the backend function

The backend is a single file, [Backend/cortex-function/bossroom-backend.js](Backend/cortex-function/bossroom-backend.js).
It is what replaces Authentication and Multiplayer Services: it signs players in, manages lobbies as
gatherings and hands out ODIN room tokens — with the keys staying on the server, never in the build.

In the Cortex dashboard, activate serverless functions for the project and create a function:

* slug `bossroom-backend`, runtime `nodejs20`, auth mode `public`
* code: the contents of `bossroom-backend.js`
* environment variables:

  | Variable | Value |
  |---|---|
  | `BOSSROOM_API_URL` | `https://cortex.odin.4players.io` |
  | `BOSSROOM_API_KEY` | the API key from step 2 |
  | `PLAYER_TOKEN_SECRET` | any random string, at least 32 characters |
  | `TRANSCRIPTION` *(optional)* | `true` to start transcription when a match begins |

  > Names starting with `CORTEX_` are reserved. Cortex injects `CORTEX_API_URL` and
  > `CORTEX_PROJECT_SECRET` itself and the runtime strips them before your code runs, so a variable of
  > that name would always read as `undefined`.

* publish and deploy

Then open the function's invoke URL in a browser. It answers with its name, the configured game and its
routes — which confirms deployment, environment and project id in one request.

[Backend/cortex-function/README.md](Backend/cortex-function/README.md) has the full endpoint list and
the notes you need when changing the function.

### 4. Point the game at it

Open `Assets/Resources/OdinSampleConfig.asset` and set **Backend Url** to the invoke URL:

```
https://cortex.odin.4players.io/invoke/<projectId>/bossroom-backend
```

That is the whole client-side configuration. Press Play.

<br>

## Trying it without a backend

Leave **Backend Url** empty and put an ODIN access key into **Development Access Key** instead
(*Tools > 4Players ODIN > Setup Netcode Transport and Voice* generates one). Lobbies are then only
reachable by join code and room tokens are created on the client. Good enough for a first look — but
never ship an access key in a build, it lets anyone create rooms in your project.

<br>

## Testing multiplayer

**In the editor:** *Window > Multiplayer > Multiplayer Play Mode*, activate one to three virtual
players. Each is a separate process with its own project path, so each signs in as its own player. No
build needed — this is the quickest way.

**With builds**, two things will otherwise stop you:

* **Tick "Development Build".** The host refuses clients whose build type differs from its own, and the
  editor always counts as a development build. A release build cannot join a host running in the editor.
* **Give every instance its own profile.** A player is identified by device id plus local profile, and
  in a build the profile is empty unless you say so — two instances on one machine would be the same
  player, and the second gets kicked. Use the **Change Profile** button, or start with a profile:

  ```bash
  open -n "Boss Room.app" --args -AuthProfile player2
  ```

  On macOS `open -n` is also what lets you launch the same app twice at all.

  The profile also names the player: picking one takes its name over as the player name, so the Cortex
  participant list shows `player2` rather than a random one. The dice next to the name still overrides it.

<br>

## Voice chat

Voice runs in the room that is already open for the netcode, so it costs no extra connection. Players
are heard positionally at their character; the party channel ignores distance.

| Key | Action |
|---|---|
| `M` | Toggle microphone mute |
| `V` (hold) | Party radio — heard by the whole party regardless of distance |

Ranges, playback distances and the channel layout sit on the `OdinNetcodeVoice` component next to the
transport on the `NetworkingManager` prefab. With `TRANSCRIPTION` enabled on the function, the overlay
also shows the live transcript with moderation flags.

<br>

## Where to look in the code

The ODIN-specific code is deliberately kept in one place, so the diff is readable.

| Path | What it is |
|---|---|
| [Assets/Scripts/OdinServices/Backend/](Assets/Scripts/OdinServices/Backend) | HTTP client for the function. `CortexLobbyBackend` is the real one, `LocalLobbyBackend` is the access-key mode |
| [Assets/Scripts/OdinServices/Auth/PlayerAuthFacade.cs](Assets/Scripts/OdinServices/Auth/PlayerAuthFacade.cs) | Sign-in, replaces `AuthenticationServiceFacade` |
| [Assets/Scripts/OdinServices/Sessions/GatheringsFacade.cs](Assets/Scripts/OdinServices/Sessions/GatheringsFacade.cs) | Lobbies as gatherings, replaces `MultiplayerServicesFacade` |
| [Assets/Scripts/ConnectionManagement/ConnectionMethod.cs](Assets/Scripts/ConnectionManagement/ConnectionMethod.cs) | `ConnectionMethodOdin` — the only connection method left |
| [Backend/cortex-function/](Backend/cortex-function) | The serverless function and its tests |
| `Packages/io.fourplayers.odin.netcode` *(resolved from git)* | `OdinNetcodeTransport`, the socket protocol, the voice components |

Run the function's tests with:

```bash
node --test Backend/cortex-function/test
```

<br>

## Status

Playable end to end: lobby list, join code, quick join, character select, the fight, post game, and
proximity voice throughout. Two caveats worth knowing:

* A handful of PlayMode tests fail in this fork. They fail the same way on the upstream sample at the
  commit this fork branched from — they are not caused by the ODIN work, and the tests say so.
* Both ODIN packages point at feature branches that have not been merged upstream yet.

<br>

## License

Unchanged from the upstream sample, see [LICENSE.md](LICENSE.md).
