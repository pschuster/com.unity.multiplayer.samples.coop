using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OdinNative.Netcode;
using OdinNative.Unity;
using OdinNative.Wrapper.Room;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.OdinServices.Cortex;
using Unity.Netcode;
using UnityEngine;
using VContainer;
using ChannelMask = OdinNative.Core.Utility.ChannelMask;

namespace Unity.BossRoom.Gameplay.Cortex
{
    /// <summary>
    /// Enforces ODIN Cortex sanctions in the voice room: stops this client from hearing muted players and tells
    /// the local player about their own sanctions. This is the Boss Room reference implementation of the Cortex
    /// Room Protocol (docs: Room Protocol, Sanctions#Enforcement in the Cortex vault).
    /// </summary>
    /// <remarks>
    /// <para>Why it exists: Cortex stores sanctions, but it cannot take a player's voice away on its own. With the
    /// project setting <c>sanctionPush</c>, the Cortex transcription bot in the room broadcasts who is muted, and
    /// every client sets that peer's listen channel mask to <see cref="ChannelMask.None"/>. The ODIN server then
    /// stops delivering the peer's audio to that client. A patched client of the muted player cannot get around
    /// this, because the receivers decide. The bot also stops transcribing a muted player.</para>
    ///
    /// <para>What it does:</para>
    /// <list type="bullet">
    /// <item>Finds the bot peer: the ODIN tag <c>cortex:bot</c>, or the bot's user id when the project's token
    /// provider cannot sign tags. Only messages from that peer are treated as Cortex frames.</item>
    /// <item>Keeps a <see cref="CortexMuteSet"/>: <c>cortex.mutes</c> replaces it, <c>cortex.sanction</c> adds or
    /// removes peers, and a peer joining with the tag <c>cortex:muted</c> (set by the Cortex join gate) is muted
    /// before the bot has said anything. Every change is applied as a per-peer listen channel mask.</item>
    /// <item>For the local player's own sanctions, shows a warning or mute notice in the <see cref="OdinVoiceHud"/>,
    /// or, on a ban, shows a popup and leaves the game. Rejoining is then refused by the join gate in the backend
    /// function.</item>
    /// <item>With the project setting <c>transcriptPush</c>, hands every <c>cortex.transcript</c> line to the HUD
    /// the moment the bot sends it, instead of the HUD waiting for its next poll. After missed frames or a bot
    /// rejoin it asks the HUD to catch up through polling.</item>
    /// </list>
    ///
    /// <para>Needs an ODIN Unity SDK with odin-sdk-unity#2 (<c>MessageReceived</c> is raised); older versions never
    /// deliver the bot's messages. It also needs the transcription bot in the room, i.e. an active Cortex session
    /// (<c>TRANSCRIPTION=true</c> in the backend function).</para>
    /// </remarks>
    public class CortexRoomListener : MonoBehaviour
    {
        /// <summary>How long a warning stays on screen.</summary>
        const float k_NoticeSeconds = 10f;

        [Inject]
        NetworkManager m_NetworkManager;
        [Inject]
        ConnectionManager m_ConnectionManager;

        readonly CortexMuteSet m_Mutes = new CortexMuteSet();
        readonly List<uint> m_Added = new List<uint>();
        readonly List<uint> m_Removed = new List<uint>();

        OdinVoiceHud m_Hud;
        OdinNetcodeTransport m_Transport;
        OdinRoom m_Room;
        /// <summary>The Cortex bot's peer in the current room, 0 while it is not there.</summary>
        uint m_BotPeerId;
        /// <summary>Whether the bot was recognised by its signed tag rather than only by its user id.</summary>
        bool m_BotHasTag;
        /// <summary>Frame counter of the last frame from the bot, to notice missed frames.</summary>
        long m_LastSeq;

        void Start()
        {
            m_Hud = GetComponent<OdinVoiceHud>();
        }

        void Update()
        {
            // the NetworkManager keeps its transport for the whole session, but bind lazily in case it is set late
            var transport = m_NetworkManager != null ? m_NetworkManager.NetworkConfig.NetworkTransport as OdinNetcodeTransport : null;
            if (transport != m_Transport)
            {
                Bind(transport);
            }
        }

        void OnDestroy()
        {
            Bind(null);
        }

        void Bind(OdinNetcodeTransport transport)
        {
            if (m_Transport != null)
            {
                m_Transport.RoomCreated -= OnRoomCreated;
                m_Transport.RoomDestroying -= OnRoomDestroying;
                if (m_Room != null)
                {
                    OnRoomDestroying(m_Room);
                }
            }

            m_Transport = transport;
            if (m_Transport == null)
            {
                return;
            }

            m_Transport.RoomCreated += OnRoomCreated;
            m_Transport.RoomDestroying += OnRoomDestroying;
            if (m_Transport.Room != null)
            {
                OnRoomCreated(m_Transport.Room);
            }
        }

        void OnRoomCreated(OdinRoom room)
        {
            m_Room = room;
            // the transport creates these proxies before raising RoomCreated; guard anyway, a missing one only costs a feature
            room.OnPeerJoined?.AddListener(OnPeerJoined);
            room.OnPeerLeft?.AddListener(OnPeerLeft);
            room.OnMessageReceived?.AddListener(OnMessageReceived);
        }

        void OnRoomDestroying(OdinRoom room)
        {
            if (m_Room != room)
            {
                return;
            }

            room.OnPeerJoined?.RemoveListener(OnPeerJoined);
            room.OnPeerLeft?.RemoveListener(OnPeerLeft);
            room.OnMessageReceived?.RemoveListener(OnMessageReceived);
            m_Room = null;
            m_BotPeerId = 0;
            m_BotHasTag = false;
            m_LastSeq = 0;
            // the room and its channel masks go away with it
            m_Mutes.Clear();
            PublishModeratedPeers();
        }

        void OnPeerJoined(object sender, PeerJoinedEventArgs args)
        {
            var peerId = args.peer_id;
            // OdinRoom does not copy the tags into its event, but the wrapper room has filled RemotePeers by now
            var tags = m_Transport.BaseRoom != null && m_Transport.BaseRoom.RemotePeers.TryGetValue(peerId, out var peer)
                ? peer.Tags
                : null;

            var hasBotTag = tags != null && tags.Contains(CortexRoomProtocol.k_BotTag);
            // the tag is signed by Cortex and cannot be forged; the user id is only a fallback for projects whose
            // token provider cannot sign tags, and never replaces a bot that was recognised by its tag
            if (hasBotTag || (!m_BotHasTag && args.user_id == CortexRoomProtocol.k_DefaultBotUserId))
            {
                m_BotPeerId = peerId;
                m_BotHasTag = hasBotTag;
                m_LastSeq = 0;
                Debug.Log($"[Cortex] Bot joined the room as peer {peerId}{(hasBotTag ? string.Empty : " (identified by user id only)")}");
                // a (re)joining bot may have transcribed lines while it was not in the room
                if (m_Hud != null)
                {
                    m_Hud.RequestTranscriptCatchUp();
                }

                return;
            }

            // the join gate tagged a muted player's token: mask them before the bot says anything
            if (tags != null && tags.Contains(CortexRoomProtocol.k_MutedTag))
            {
                m_Added.Clear();
                m_Mutes.LocalPeerId = m_Transport.LocalPeerId;
                m_Mutes.Set(new[] { peerId }, true, m_Added);
                ApplyMasks(m_Added, null);
            }
        }

        void OnPeerLeft(object sender, PeerLeftEventArgs args)
        {
            if (args.PeerId == m_BotPeerId)
            {
                m_BotPeerId = 0;
                m_BotHasTag = false;
                m_LastSeq = 0;
            }

            // ODIN drops the peer's channel mask override by itself
            m_Mutes.Forget(args.PeerId);
            PublishModeratedPeers();
        }

        void OnMessageReceived(object sender, MessageReceivedEventArgs args)
        {
            // the namespace is the sender: only the bot speaks Cortex, everything else belongs to the game
            if (m_BotPeerId == 0 || args.PeerId != m_BotPeerId || !CortexRoomProtocol.TryParse(args.Data, out var frame))
            {
                return;
            }

            if (m_LastSeq != 0 && frame.seq != m_LastSeq + 1)
            {
                // transcript lines are caught up through polling; mutes resync with the next cortex.mutes (on any join)
                Debug.LogWarning($"[Cortex] Missed {frame.seq - m_LastSeq - 1} frame(s) from the bot (seq {m_LastSeq} -> {frame.seq})");
                if (m_Hud != null)
                {
                    m_Hud.RequestTranscriptCatchUp();
                }
            }

            m_LastSeq = frame.seq;
            m_Mutes.LocalPeerId = m_Transport.LocalPeerId;

            switch (frame.type)
            {
                case CortexRoomProtocol.k_TypeMutes:
                    OnMutes(frame.data);
                    break;
                case CortexRoomProtocol.k_TypeSanction:
                    OnSanction(frame.data);
                    break;
                case CortexRoomProtocol.k_TypeTranscript:
                    if (m_Hud != null)
                    {
                        m_Hud.AddPushedTranscript(frame.data.segmentId, frame.data.peerId, frame.data.text, frame.data.interim, frame.data.ts);
                    }

                    break;
                // custom game messages (types outside cortex.*) are ignored here
            }
        }

        void OnMutes(CortexFrameData data)
        {
            var mutes = data.mutes ?? Array.Empty<CortexMute>();
            m_Added.Clear();
            m_Removed.Clear();
            m_Mutes.Replace(mutes.Select(m => m.peerId), m_Added, m_Removed);
            ApplyMasks(m_Added, m_Removed);

            // our own entry is there when we joined while muted (a shadow mute never shows up for the muted player)
            var own = mutes.FirstOrDefault(m => m.peerId != 0 && m.peerId == m_Transport.LocalPeerId);
            if (own != null)
            {
                ShowNotice($"<color=#ff9f43>You are muted by moderation{Until(own.endAt)}.</color> Other players cannot hear you.", 0);
            }
        }

        void OnSanction(CortexFrameData data)
        {
            var sanction = data.sanction;
            if (sanction == null)
            {
                return;
            }

            var peerIds = data.peerIds ?? Array.Empty<uint>();
            var isOwn = m_Transport.LocalPeerId != 0 && peerIds.Contains(m_Transport.LocalPeerId);

            if (CortexRoomProtocol.IsVoiceMute(sanction.type))
            {
                // enforcement broadcast: everyone masks the muted player's peers (the mute set skips our own peer)
                m_Added.Clear();
                m_Mutes.Set(peerIds, data.mute, m_Added);
                if (data.mute)
                {
                    ApplyMasks(m_Added, null);
                }
                else
                {
                    ApplyMasks(null, m_Added);
                }

                if (isOwn)
                {
                    ShowNotice(data.mute
                        ? $"<color=#ff9f43>You are muted by moderation{Until(sanction.endAt)}.</color> Other players cannot hear you.{Reason(sanction.reason)}"
                        : "<color=#7CFC00>Your mute has ended.</color> Other players can hear you again.", data.mute ? 0 : k_NoticeSeconds);
                }

                return;
            }

            // everything else only reaches the sanctioned player's own peers, as a notice
            if (!isOwn || data.action == "revoked" || data.action == "expired")
            {
                return;
            }

            if (CortexRoomProtocol.IsBan(sanction.type))
            {
                OnBanned(sanction);
            }
            else if (sanction.type == "warn")
            {
                ShowNotice($"<color=#ffd700>Warning from moderation.</color>{Reason(sanction.reason)}", k_NoticeSeconds);
            }
        }

        void OnBanned(CortexSanction sanction)
        {
            // leaving is on us: ODIN has no server-side kick yet. The join gate refuses the next room token.
            Debug.Log($"[Cortex] Banned ({sanction.type}), leaving the game");
            PopupManager.ShowPopupPanel("Banned", $"You were banned from this game{Until(sanction.endAt)}.{Reason(sanction.reason)}");
            m_ConnectionManager.RequestShutdown();
        }

        /// <summary>
        /// Applies mute set changes as ODIN listen channel masks: the server stops sending us a muted peer's audio.
        /// </summary>
        void ApplyMasks(List<uint> mute, List<uint> unmute)
        {
            var room = m_Transport != null ? m_Transport.BaseRoom : null;
            if (room != null)
            {
                // per-peer overrides survive later joins; a raw SetChannelMasks would be reset by the next PeerJoined
                if (mute != null)
                {
                    foreach (var peerId in mute)
                    {
                        room.SetListenChannelMaskForPeer(peerId, ChannelMask.None);
                    }
                }

                if (unmute != null)
                {
                    foreach (var peerId in unmute)
                    {
                        room.ClearListenChannelMaskForPeer(peerId);
                    }
                }
            }

            PublishModeratedPeers();
        }

        void PublishModeratedPeers()
        {
            if (m_Hud != null)
            {
                m_Hud.ModeratedPeers = m_Mutes.Muted.ToArray();
            }
        }

        void ShowNotice(string message, float seconds)
        {
            if (m_Hud != null)
            {
                m_Hud.ShowNotice(message, seconds);
            }
        }

        /// <summary>" until 24 Sep, 16:00" in local time, or " permanently" without an end.</summary>
        static string Until(string endAt)
        {
            if (string.IsNullOrEmpty(endAt))
            {
                return " permanently";
            }

            return DateTime.TryParse(endAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var end)
                ? $" until {end.ToLocalTime():g}"
                : string.Empty;
        }

        static string Reason(string reason) => string.IsNullOrEmpty(reason) ? string.Empty : $"\n<size=80%>{reason}</size>";
    }
}
