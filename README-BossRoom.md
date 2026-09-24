![Banner](Documentation/Images/Banner.png)
<br><br>

# Boss Room: a Co-op, Multiplayer RPG Sample
###  Made with and Including Utilities for Netcode for GameObjects
<br>

[![UnityVersion](https://img.shields.io/badge/Unity%20Version:-6000.6.1f1-57b9d3.svg?logo=unity&color=2196F3)](https://unity.com/releases/editor/archive)
[![NetcodeVersion](https://img.shields.io/badge/Netcode%20Version:-2.13.2-57b9d3.svg?logo=unity&color=2196F3)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/releases/tag/v2.13.2)
[![LatestRelease](https://img.shields.io/badge/Latest%20Github%20Release:-v3.0.0-57b9d3.svg?logo=github&color=brightgreen)](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop/releases/tag/v3.0.0)
<br><br>

Boss Room is a fully functional co-op multiplayer RPG made with Unity Netcode. It is an educational sample designed to showcase typical netcode [patterns](https://docs-multiplayer.unity3d.com/netcode/current/learn/bossroom/bossroom-actions/index.html) that are frequently featured in similar multiplayer games.
<br><br>

# Boss Room Sample Overview

Boss Room is designed to be used in its entirety to help you explore the concepts and patterns behind a multiplayer game flow; such as character abilities, casting animations to hide latency, replicated objects, RPCs, and integration with [4Players ODIN](https://www.4players.io/odin/) for networking, voice chat and lobbies. This fork replaces every Unity Gaming Service: game traffic runs over ODIN sockets, lobbies are ODIN Cortex gatherings, and proximity voice chat shares the same ODIN room.

You can use the project as a reference starting point for your own Unity game or use elements individually.
<br><br>

---

### 💡 Utilities Package
This repository also contains a [Utilities](Packages/com.unity.multiplayer.samples.coop) package, containing reusable sample scripts. You can install it using the following manifest file entry:
<br>
`"com.unity.multiplayer.samples.coop": "https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop.git?path=/Packages/com.unity.multiplayer.samples.coop",`

---


<br>

For more information on the art of Boss Room, see [ART_NOTES.md](Documentation/ART_NOTES.md).


![](Documentation/Images/Boss.png)
<br><br>
  
-----

## Readme Contents and Quick Links
<!-- TOC generated from https://luciopaiva.com/markdown-toc/ -->
<details open>
<summary> <b>Click to expand/collapse contents</b> </summary>

- ### [Getting the project](#getting-the-project-1)
  - [Cloning the project](#cloning-the-project)
  - [The two ODIN packages](#the-two-odin-packages)
- ### [Requirements](#requirements-1)
  - [Min Spec Devices](#boss-rooms-min-spec-devices-are)
- ### [Opening the project for the first time](#opening-the-project-for-the-first-time-1) 
- ### [Exploring the project](#exploring-the-project-1)
  - [Setting up ODIN](#setting-up-odin)
- ### [Testing multiplayer](#testing-multiplayer-1) 
  - [In the editor with Multiplayer Play Mode](#in-the-editor-with-multiplayer-play-mode)
  - [With builds](#with-builds)
  - [Multiplayer over Internet](#multiplayer-over-internet)
- ### [Index of resources in this project](#index-of-resources-in-this-project-1)
  - [Gameplay](#gameplay)
  - [Game Flow](#game-flow)
  - [Connectivity](#connectivity)
  - [ODIN services (lobbies, tokens, voice)](#odin-services-lobbies-tokens-voice)
  - [Tools and Utilities](#tools-and-utilities)
- ### [Troubleshooting](#troubleshooting-1)
  - [Bugs](#bugs)
  - [Documentation](#documentation)
- ### [License](#license-1)
- ### [Contributing](#contributing-1)
- ### [Community](#community-1)
- ### [Feedback Form](#feedback-form-1)
- ### [Other samples](#other-samples-1)
  - [Bitesize Samples](#bitesize-samples)
</details>

------
<br>

## Getting the project
### Cloning the project

This ODIN fork lives on GitHub. Install Git LFS **before** cloning, otherwise every scene,
prefab and texture arrives as a text pointer instead of an asset:

```
git lfs install
git clone https://github.com/pschuster/com.unity.multiplayer.samples.coop.git
```

See [Git LFS installation options](https://github.com/git-lfs/git-lfs/wiki/Installation) if you do not have it yet.

### The two ODIN packages

The package manager pulls both ODIN packages from git, pinned to a commit in
[Packages/manifest.json](Packages/manifest.json):

| Package | Source |
|---------|--------|
| `io.fourplayers.odin` | `github.com/4Players/odin-sdk-unity`, branch `feature/socket-inbound-info` |
| `io.fourplayers.odin.netcode` | `gitlab.4players.de/odin/integrations/odin-transport-for-unity-ngo`, branch `feature/odin-sockets` |

Both branches carry work that has not been merged upstream yet: the SDK fills in the socket info for
inbound sockets, and the transport is the rewrite on the ODIN Sockets API.

Unity runs your own `git` for this, so you need git on the PATH and read access to the GitLab repository.
To move to a newer version of either package, replace the hash after the `#` in the manifest.
<br><br>

## Requirements

This fork is on **Unity 6000.6.1f1**. Please include standalone support for Windows/Mac in your installation.

**PLEASE NOTE:** You will also need Netcode for Game Objects to use these samples. See the [Installation Documentation](https://docs-multiplayer.unity3d.com/netcode/current/installation) to prepare your environment. You can also complete the [Get Started With NGO](https://docs-multiplayer.unity3d.com/netcode/current/tutorials/get-started-ngo) tutorial to familiarize yourself with Netcode For Game Objects.
<br><br>

#### Boss Room has been developed and tested on the following platforms:
- Windows
- Mac
- iOS
- Android <br>

#### Boss Room's min spec devices are:
- iPhone 6S
- Samsung Galaxy J2 Core 
<br><br>

### Installing Git LFS to clone locally

Boss Room uses Git Large Files Support (LFS) to handle all large assets required locally. See [Git LFS installation options](https://github.com/git-lfs/git-lfs/wiki/Installation) for Windows and Mac instructions. This step is only needed if cloning locally. You can also just download the project which will already include large files.
<br><br>



## Opening the project for the first time

Once you have downloaded the project, follow the steps below to get up and running:
 - Check that you have installed Unity 6000.6.1f1.
 	- Include standalone support for Windows/Mac in your installation. 
 - Add the project to the _Unity Hub_ by clicking on the **Add** button and pointing it to the root folder of the downloaded project.
 	- __Please note :__ the first time you open the project Unity will import all assets, which will take longer than usual.

 - Hit the **Play** button. You can then host a new game or join an existing one using the in-game UI.

  ![](Documentation/Images/StartupScene.png)
<br><br><br>

## Exploring the project
BossRoom is an eight-player co-op RPG game experience, where players collaborate to fight imps, and then a boss. Players can select between classes that each have skills with didactically interesting networking characteristics. Control model is click-to-move, with skills triggered by a mouse button or hotkey.

One of the eight clients acts as the host/server. That client will use a compositional approach so that its entities have both server and client components.

- The game is server-authoritative, with latency-masking animations. 
- Position updates are carried out through NetworkTransform that sync position and rotation. 

Code is organized in domain-based assemblies. See the [Boss Room architecture documentation](https://docs-multiplayer.unity3d.com/netcode/current/learn/bossroom/bossroom-architecture) file for more details.
<br><br>

### Setting up ODIN

Boss Room connects players through [4Players ODIN](https://www.4players.io/odin/), so no Unity service and no relay,
port forwarding or dedicated server is needed. Settings live in `Assets/Resources/OdinSampleConfig.asset`:

* **Backend Url** — invoke URL of the Boss Room backend function on ODIN Cortex, e.g.
  `https://cortex.odin.4players.io/invoke/<projectId>/bossroom-backend`. It provides sign-in, the lobby list,
  join codes and ODIN room tokens. See [Backend/cortex-function/README.md](Backend/cortex-function/README.md) for deployment.
* **Development Access Key** — used when no backend URL is set. Lobbies are then reachable by join code only and tokens
  are generated on the client. Convenient for a first test, but never ship an access key in a build.

Get an account and a project in the [ODIN console](https://console.4players.io).

#### Voice chat

Voice runs in the same ODIN room as the game traffic, so it needs no extra connection. In game, players are heard
positionally at their character (proximity channel). An overlay shows the microphone state and who is talking:

| Key | Action |
|-----|--------|
| `M` | Toggle microphone mute |
| `V` (hold) | Party radio: heard by the whole party regardless of distance |

Proximity range, playback distances and the push-to-talk channels are configured on the `OdinNetcodeVoice`
component next to the transport on the `NetworkingManager` prefab. With transcription enabled on the backend, the
overlay also shows the live transcript with moderation flags from ODIN Cortex.
<br><br><br>
 
## Testing multiplayer

In order to see the multiplayer functionality in action we can either run multiple instances of the game locally on your computer - using either Multiplayer Play Mode or builds - or choose to connect to a friend over the internet. See [how to test](https://docs-multiplayer.unity3d.com/netcode/current/tutorials/testing/testing_locally) for more info.
<br><br>

### In the editor with Multiplayer Play Mode

The quickest way: **Window > Multiplayer > Multiplayer Play Mode**, then activate one to three virtual
players. Each one is a separate process with its own project path, which means each signs in as its own
player automatically. No build needed.

### With builds

Build an executable through **File > Build Profiles**, then start it next to the editor or several times
side by side. Two things will otherwise stop you:

**Check "Development Build".** The host refuses a client whose build type differs from its own
(`IncompatibleBuildType`), and the editor always counts as a development build. Without that checkbox, a
player built for release cannot join a host running in the editor.

**Give every instance its own profile.** Sign-in derives the player from the device id plus the local
profile, and in a build the profile is empty unless you say otherwise. Two instances on one machine would
be the same player, and the host kicks the second one out as `LoggedInAgain`. Either use the
**Change Profile** button in the main menu, or pass the profile on startup:

```
open -n "Boss Room.app" --args -AuthProfile player2
```

On macOS, `open -n` is also what lets you start the same app twice in the first place.
<br>

### Multiplayer over Internet

To play over internet, first build an executable that is shared between all players - as above.

It is possible to connect between multiple instances of the same executable OR between executables and the editor that produced it.

Playing over the internet needs no extra setup: host and clients join the same ODIN room and exchange all Netcode
traffic through ODIN sockets, so ODIN's servers handle connectivity. No relay allocation, no port forwarding and no
public IP are required. Players find each other through the lobby list, a join code or quick join.
<br><br><br>

-----

## Index of resources in this project

<details open>
<summary> <b>Click to expand/collapse contents</b> </summary>

### Gameplay
* Action anticipation - AnticipateActionClient() in [Assets/Scripts/Gameplay/Action/Action.cs](Assets/Scripts/Gameplay/Action/Action.cs)
* Object spawning for  long actions (archer arrow) - LaunchProjectile() in [Assets/Scripts/Gameplay/Action/ConcreteActions/LaunchProjectileAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/LaunchProjectileAction.cs)
* Quick actions with  RPCs (ex: mage bolt) - [Assets/Scripts/Gameplay/Action/ConcreteActions/FXProjectileTargetedAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/FXProjectileTargetedAction.cs)
* Teleport - [Assets/Scripts/Gameplay/Action/ConcreteActions/DashAttackAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/DashAttackAction.cs)
* Client side input tracking  before an action (archer AOE) - OnStartClient() in [Assets/Scripts/Gameplay/Action/ConcreteActions/AOEAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/AOEAction.cs)
* Time based action (charged shot) - [Assets/Scripts/Gameplay/Action/ConcreteActions/ChargedLaunchProjectileAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/ChargedLaunchProjectileAction.cs)
* Object parenting to animation - [Assets/Scripts/Gameplay/Action/ConcreteActions/PickUpAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/PickUpAction.cs)
* Physics object throwing (using NetworkRigidbody) - [Assets/Scripts/Gameplay/Action/ConcreteActions/TossAction.cs ](Assets/Scripts/Gameplay/Action/ConcreteActions/TossAction.cs)
* NetworkAnimator usage - All actions, in particular [Assets/Scripts/Gameplay/Action/ConcreteActions/ChargedShieldAction.cs](Assets/Scripts/Gameplay/Action/ConcreteActions/ChargedShieldAction.cs)
* NetworkTransform local space - [Assets/Scripts/Gameplay/GameplayObjects/ServerDisplacerOnParentChange.cs](Assets/Scripts/Gameplay/GameplayObjects/ServerDisplacerOnParentChange.cs)
* Dynamic imp spawning with portals - [Assets/Scripts/Gameplay/GameplayObjects/ServerWaveSpawner.cs ](Assets/Scripts/Gameplay/GameplayObjects/ServerWaveSpawner.cs)
* In scene placed dynamic objects (imps) - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetworkObjectSpawner.cs ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetworkObjectSpawner.cs)
* Static objects (non-destroyables like doors, switches, etc) - [Assets/Scripts/Gameplay/GameplayObjects/SwitchedDoor.cs](Assets/Scripts/Gameplay/GameplayObjects/SwitchedDoor.cs)
* State tracking with breakables, switch, doors
  * [Assets/Scripts/Gameplay/GameplayObjects/Breakable.cs ](Assets/Scripts/Gameplay/GameplayObjects/Breakable.cs)
  * [Assets/Scripts/Gameplay/GameplayObjects/FloorSwitch.cs](Assets/Scripts/Gameplay/GameplayObjects/FloorSwitch.cs)
  * [Assets/Scripts/Gameplay/GameplayObjects/SwitchedDoor.cs](Assets/Scripts/Gameplay/GameplayObjects/SwitchedDoor.cs)
* NetworkVariable with Enum - [Assets/Scripts/Gameplay/GameState/NetworkPostGame.cs](Assets/Scripts/Gameplay/GameState/NetworkPostGame.cs)
* NetworkVariable with custom serialization (GUID) - [Assets/Scripts/Infrastructure/NetworkGuid.cs](Assets/Scripts/Infrastructure/NetworkGuid.cs)
* NetworkVariable with fixed string - [Assets/Scripts/Utils/NetworkNameState.cs](Assets/Scripts/Utils/NetworkNameState.cs)
* NetworkList with custom serialization (LobbyPlayerState) - [Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs](Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs)
* Persistent player (over multiple scenes) - [Assets/Scripts/Gameplay/GameplayObjects/PersistentPlayer.cs ](Assets/Scripts/Gameplay/GameplayObjects/PersistentPlayer.cs)
* Character logic (including player's avatar) - [Assets/Scripts/Gameplay/GameplayObjects/Character/ ](Assets/Scripts/Gameplay/GameplayObjects/Character/) <br> [ Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacter.cs ](Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacter.cs)
* Character movements - [Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacterMovement.cs](Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacterMovement.cs)
* Client driven movements - Boss Room is server driven with anticipation animation. See [Client Driven bitesize](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.bitesize/tree/main/Basic/ClientDriven) for client driven gameplay
* Player spawn - SpawnPlayer() in [Assets/Scripts/Gameplay/GameState/ServerBossRoomState.cs](Assets/Scripts/Gameplay/GameState/ServerBossRoomState.cs)
* Player camera setup (with cinemachine) - OnNetworkSpawn() in [Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs](Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs)
* INetworkSerializable (bandwidth optimization) vs INetworkSerializeByMemcpy (performance optimization) usage. See LobbyPlayerState vs ActionID structs [Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs](Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs) vs [Assets/Scripts/Gameplay/Action/ActionID.cs](Assets/Scripts/Gameplay/Action/ActionID.cs)

### Game Flow
* Application Controller - [Assets/Scripts/ApplicationLifecycle/ApplicationController.cs ](Assets/Scripts/ApplicationLifecycle/ApplicationController.cs)
* Game flow state machine - All child classes in [Assets/Scripts/Gameplay/GameState/GameStateBehaviour.cs ](Assets/Scripts/Gameplay/GameState/GameStateBehaviour.cs)
* Scene loading and progress sharing - [ackages/com.unity.multiplayer.samples.coop/Utilities/SceneManagement/](Packages/com.unity.multiplayer.samples.coop/Utilities/SceneManagement/)
* Synced UI with character select - [Assets/Scripts/Gameplay/GameState/ClientCharSelectState.cs ](Assets/Scripts/Gameplay/GameState/ClientCharSelectState.cs)
* In-game lobby (character selection) - [Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs ](Assets/Scripts/Gameplay/GameState/NetworkCharSelection.cs)<br>[Assets/Scripts/Gameplay/GameState/ServerCharSelectState.cs](Assets/Scripts/Gameplay/GameState/ServerCharSelectState.cs)
* Win state - [Assets/Scripts/Gameplay/GameState/PersistentGameState.cs](Assets/Scripts/Gameplay/GameState/PersistentGameState.cs)

### Connectivity
* Disconnecting every client with reason - OnUserRequestedShutdown() in [Assets/Scripts/ConnectionManagement/ConnectionState/HostingState.cs ](Assets/Scripts/ConnectionManagement/ConnectionState/HostingState.cs)
* Connection approval with reason sent to the client when denied - ApprovalCheck() in [Assets/Scripts/ConnectionManagement/ConnectionState/HostingState.cs ](Assets/Scripts/ConnectionManagement/ConnectionState/HostingState.cs)
* Connection state machine with error handling - [Assets/Scripts/ConnectionManagement/ConnectionManager.cs ](Assets/Scripts/ConnectionManagement/ConnectionManager.cs) <br> [Assets/Scripts/ConnectionManagement/ConnectionState/](Assets/Scripts/ConnectionManagement/ConnectionState/)
* ODIN transport setup - ConnectionMethodOdin in [Assets/Scripts/ConnectionManagement/ConnectionMethod.cs](Assets/Scripts/ConnectionManagement/ConnectionMethod.cs)
* Netcode transport on ODIN sockets - OdinNetcodeTransport in the `io.fourplayers.odin.netcode` package
* Session manager - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/SessionManager.cs ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/SessionManager.cs)
* RTT stats - [Assets/Scripts/Utils/NetworkOverlay/NetworkStats.cs](Assets/Scripts/Utils/NetworkOverlay/NetworkStats.cs)

### ODIN services (lobbies, tokens, voice)
* Lobby - host creation - CreateSessionRequest() in [Assets/Scripts/Gameplay/UI/Session/SessionUIMediator.cs ](Assets/Scripts/Gameplay/UI/Session/SessionUIMediator.cs)
* Lobby - client join - JoinSessionRequest() in [Assets/Scripts/Gameplay/UI/Session/SessionUIMediator.cs ](Assets/Scripts/Gameplay/UI/Session/SessionUIMediator.cs)
* Lobbies, room tokens and tracking - [Assets/Scripts/OdinServices/Sessions/GatheringsFacade.cs](Assets/Scripts/OdinServices/Sessions/GatheringsFacade.cs)
* Cortex gathering calls and local development fallback - [Assets/Scripts/OdinServices/Backend](Assets/Scripts/OdinServices/Backend)
* Player sign-in - [Assets/Scripts/OdinServices/Auth/PlayerAuthFacade.cs](Assets/Scripts/OdinServices/Auth/PlayerAuthFacade.cs)
* Profile management for local instances - GetProfile() in [Assets/Scripts/Utils/ProfileManager.cs](Assets/Scripts/Utils/ProfileManager.cs)
* Voice chat, proximity and channels - OdinNetcodeVoice and OdinNetworkPlayerVoice in the `io.fourplayers.odin.netcode` package
* Voice overlay and Cortex transcript - [Assets/Scripts/Gameplay/UI/OdinVoiceHud.cs](Assets/Scripts/Gameplay/UI/OdinVoiceHud.cs)
* Backend function (sign-in, lobbies, tokens, transcript) - [Backend/cortex-function](Backend/cortex-function)
* Profile manager for local play [Assets/Scripts/Utils/ProfileManager.cs](Assets/Scripts/Utils/ProfileManager.cs)

### Tools and Utilities
* Networked message channel (inter-class and networked messaging) - [Assets/Scripts/Infrastructure/PubSub/NetworkedMessageChannel.cs](Assets/Scripts/Infrastructure/PubSub/NetworkedMessageChannel.cs)
* Simple interpolation - [Assets/Scripts/Utils/PositionLerper.cs ](Assets/Scripts/Utils/PositionLerper.cs)
* Network Object Pooling - [Assets/Scripts/Infrastructure/NetworkObjectPool.cs ](Assets/Scripts/Infrastructure/NetworkObjectPool.cs)
* NetworkGuid - [Assets/Scripts/Infrastructure/NetworkGuid.cs ](Assets/Scripts/Infrastructure/NetworkGuid.cs)
* Netcode hooks - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetcodeHooks.cs ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetcodeHooks.cs)
* Spawner for in-scene objects - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetworkObjectSpawner.cs ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/NetworkObjectSpawner.cs)
* Session manager for reconnection - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/SessionManager.cs ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/SessionManager.cs)
* Client authority - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/ClientAuthority/](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/ClientAuthority/)
* Scene utils with synced loading screens - [Packages/com.unity.multiplayer.samples.coop/Utilities/SceneManagement/ ](Packages/com.unity.multiplayer.samples.coop/Utilities/SceneManagement/)
* RNSM custom config - [Packages/com.unity.multiplayer.samples.coop/Utilities/Net/RNSM/CustomNetStatsMonitorConfiguration.asset ](Packages/com.unity.multiplayer.samples.coop/Utilities/Net/RNSM/CustomNetStatsMonitorConfiguration.asset)
* NetworkSimulator usage through UI - [Assets/Scripts/Utils/NetworkSimulatorUIMediator.cs ](Assets/Scripts/Utils/NetworkSimulatorUIMediator.cs)
* Multiplayer Play Mode - [ Packages/manifest.json ](Packages/manifest.json)
</details>

-------
<br>

## Troubleshooting
  
### Bugs 
- Report bugs in Boss Room using Github [issues](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop/issues)
- Report NGO bugs using NGO Github [issues](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects)
- Report Unity bugs using the [Unity bug submission process](https://unity3d.com/unity/qa/bug-reporting).
  
### Documentation  
For a deep dive into Unity Netcode and Boss Room, visit our [documentation site](https://docs-multiplayer.unity3d.com/).
<br><br>
  
## License
Boss Room is licensed under the Unity Companion License. See [LICENSE.md](LICENSE.md) for more legal information.

For a deep dive in Unity Netcode and Boss Room, visit our [docs site](https://docs-multiplayer.unity3d.com/).
<br><br>

## Contributing
We welcome your contributions to this sample code and objects. See our [contribution guidelines](CONTRIBUTING.md) for details.
  
Our projects use the `git-flow` branching strategy:
 - our **`develop`** branch contains all active development
 - our **`main`** branch contains release versions

To get the project on your machine you need to clone the repository from GitHub using the following command-line command:
```
git clone https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop.git
```

**PLEASE NOTE:** You will need to have [Git LFS](https://git-lfs.github.com/) installed on your local machine in order to clone our repo.
<br><br>

## Community
For help, questions, networking advice, or discussions about Netcode for GameObjects and its samples, please join our [Discord Community](https://discord.com/channels/489222168727519232/1390346492019212368) or create a post in the [Unity Multiplayer Forum](https://forum.unity.com/forums/netcode-for-gameobjects.661/).
<br><br>

## Feedback Form

Thank you for cloning Boss Room and taking a look at the project. To help us improve and build better samples in the future, please consider submitting feedback about your experiences with Boss Room and let us know if you were able to learn everything you needed to today. It'll only take a couple of minutes. Thanks!

[Enter the Boss Room Feedback Form](https://unitytech.typeform.com/bossroom)
<br><br>

## Other samples
### Bitesize Samples
- The [Bitesize Samples](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.bitesize)  repository is currently being expanded and contains a collection of smaller samples and games, showcasing sub-features of NGO. You can review these samples with documentation to understand our APIs and features better.
<br><br>

[![Documentation](https://img.shields.io/badge/Unity-boss--room--docs-57b9d3.svg?logo=unity&color=2196F3)](https://docs-multiplayer.unity3d.com/netcode/current/learn/bossroom/bossroom)
[![Forums](https://img.shields.io/badge/Unity-multiplayer--forum-57b9d3.svg?logo=unity&color=2196F3)](https://forum.unity.com/forums/multiplayer.26/)
[![Discord](https://img.shields.io/discord/449263083769036810.svg?label=discord&logo=discord&color=5865F2)](https://discord.com/channels/489222168727519232/1390346492019212368)
<br><br>
  
  ![](Documentation/Images/Players.png)