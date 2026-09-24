using System;
using System.Collections.Generic;
using System.Text;
using OdinNative.Netcode;
using OdinNative.Netcode.Voice;
using TMPro;
using Unity.BossRoom.OdinServices.Backend;
using Unity.BossRoom.OdinServices.Cortex;
using Unity.BossRoom.OdinServices.Sessions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Small overlay for ODIN voice while connected: microphone state, who is talking, the party radio push-to-talk key
    /// and, if the backend runs transcription, the latest transcribed lines with moderation flags from ODIN Cortex.
    /// It also shows moderation notices for the local player (warnings, mutes) that the
    /// <see cref="Cortex.CortexRoomListener"/> receives from the Cortex bot in the room.
    ///
    /// Transcript lines come from two sources. With the Cortex project setting <c>transcriptPush</c>, the bot pushes
    /// each line into the room and the listener hands it over through <see cref="AddPushedTranscript"/>, so it shows
    /// within the speech-to-text latency. Polling the backend function stays as the fallback: every
    /// 2 s while nothing is pushed, and only every 15 s once push is active, to catch missed frames (or other
    /// players' lines if the project pushes to the speaker only).
    /// </summary>
    public class OdinVoiceHud : MonoBehaviour
    {
        const float k_TranscriptPollInterval = 2f;
        /// <summary>Safety-net poll interval while lines arrive through room push.</summary>
        const float k_TranscriptPushPollInterval = 15f;
        const int k_MaxTranscriptLines = 6;
        const Key k_MuteKey = Key.M;
        const Key k_PartyRadioKey = Key.V;

        [Inject]
        GatheringsFacade m_GatheringsFacade;
        [Inject]
        NetworkManager m_NetworkManager;

        readonly CortexTranscriptBuffer m_TranscriptLines = new CortexTranscriptBuffer(k_MaxTranscriptLines);
        readonly HashSet<uint> m_TalkingPeers = new HashSet<uint>();
        readonly StringBuilder m_Text = new StringBuilder();

        Canvas m_Canvas;
        TextMeshProUGUI m_StatusText;
        TextMeshProUGUI m_TranscriptText;
        TextMeshProUGUI m_NoticeText;
        /// <summary>When the current notice disappears; infinity keeps it until it is replaced or cleared.</summary>
        float m_NoticeHideAt;

        /// <summary>
        /// Remote peers Cortex muted for this client, set by the <see cref="Cortex.CortexRoomListener"/>, so the
        /// status can say why someone cannot be heard.
        /// </summary>
        public IReadOnlyCollection<uint> ModeratedPeers { get; set; } = Array.Empty<uint>();
        OdinNetcodeVoice m_Voice;
        string m_LastTranscriptTimestamp;
        string m_TranscriptSessionId;
        float m_NextTranscriptPoll;
        bool m_IsPolling;
        /// <summary>True once the bot pushed a transcript line in this session; polling then slows down.</summary>
        bool m_TranscriptPushActive;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            BuildUI();
        }

        void OnDestroy()
        {
            SetVoice(null);
        }

        void Update()
        {
            var connected = m_NetworkManager != null && (m_NetworkManager.IsClient || m_NetworkManager.IsServer);
            SetVoice(connected ? OdinNetcodeVoice.Instance : null);
            m_Canvas.enabled = connected && m_Voice != null;
            if (!m_Canvas.enabled)
            {
                ResetTranscript();
                ClearNotice();
                return;
            }

            HandleInput();
            UpdateStatus();
            PollTranscript();
            if (m_NoticeText.gameObject.activeSelf && Time.unscaledTime >= m_NoticeHideAt)
            {
                ClearNotice();
            }
        }

        /// <summary>
        /// Shows a moderation notice at the top of the screen, replacing the previous one.
        /// </summary>
        /// <param name="message">Rich text to show.</param>
        /// <param name="seconds">How long to show it; 0 or less keeps it until it is replaced or cleared.</param>
        public void ShowNotice(string message, float seconds)
        {
            m_NoticeText.SetText(message);
            m_NoticeText.gameObject.SetActive(true);
            m_NoticeHideAt = seconds > 0 ? Time.unscaledTime + seconds : float.PositiveInfinity;
        }

        /// <summary>
        /// Removes the moderation notice.
        /// </summary>
        public void ClearNotice()
        {
            if (m_NoticeText != null)
            {
                m_NoticeText.gameObject.SetActive(false);
            }
        }

        void SetVoice(OdinNetcodeVoice voice)
        {
            if (m_Voice == voice)
            {
                return;
            }

            if (m_Voice != null)
            {
                m_Voice.PeerTalkingChanged -= OnPeerTalkingChanged;
            }

            m_TalkingPeers.Clear();
            m_Voice = voice;

            if (m_Voice != null)
            {
                m_Voice.PeerTalkingChanged += OnPeerTalkingChanged;
            }
        }

        void OnPeerTalkingChanged(uint peerId, bool isTalking)
        {
            if (isTalking)
            {
                m_TalkingPeers.Add(peerId);
            }
            else
            {
                m_TalkingPeers.Remove(peerId);
            }
        }

        void HandleInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[k_MuteKey].wasPressedThisFrame)
            {
                m_Voice.IsMuted = !m_Voice.IsMuted;
            }

            // Boss Room has a single party, so the team channel acts as a party-wide radio
            m_Voice.SetPushToTalk(keyboard[k_PartyRadioKey].isPressed ? OdinVoiceChannel.Team : null);
        }

        void UpdateStatus()
        {
            m_Text.Clear();
            m_Text.Append(m_Voice.IsMuted ? "<color=#ff6666>Mic muted</color>" : "Mic on");
            m_Text.Append($"  <size=70%>[{k_MuteKey}] mute  [hold {k_PartyRadioKey}] party radio</size>\n");
            m_Text.Append("Talking on: ").Append(ChannelLabel(m_Voice.ActiveChannel));

            if (m_TalkingPeers.Count > 0)
            {
                var transport = m_NetworkManager.NetworkConfig.NetworkTransport as OdinNetcodeTransport;
                m_Text.Append("\n<color=#7CFC00>Speaking:</color> ");
                var first = true;
                foreach (var peerId in m_TalkingPeers)
                {
                    if (!first)
                    {
                        m_Text.Append(", ");
                    }

                    first = false;
                    m_Text.Append(transport != null && transport.TryGetPeerName(peerId, out var name) ? name : $"Peer {peerId}");
                }
            }

            if (ModeratedPeers.Count > 0)
            {
                var transport = m_NetworkManager.NetworkConfig.NetworkTransport as OdinNetcodeTransport;
                m_Text.Append("\n<color=#ff9f43>Muted by moderation:</color> ");
                var first = true;
                foreach (var peerId in ModeratedPeers)
                {
                    if (!first)
                    {
                        m_Text.Append(", ");
                    }

                    first = false;
                    m_Text.Append(transport != null && transport.TryGetPeerName(peerId, out var name) ? name : $"Peer {peerId}");
                }
            }

            m_StatusText.SetText(m_Text);
        }

        static string ChannelLabel(OdinVoiceChannel channel) => channel switch
        {
            OdinVoiceChannel.Proximity => "nearby players",
            OdinVoiceChannel.Team => "<color=#ffd700>party radio</color>",
            OdinVoiceChannel.Global => "everyone",
            _ => "lobby",
        };

        async void PollTranscript()
        {
            var lobby = m_GatheringsFacade.CurrentLobby;
            if (!m_GatheringsFacade.SupportsTranscription || lobby == null || string.IsNullOrEmpty(lobby.sessionId))
            {
                m_TranscriptText.gameObject.SetActive(false);
                return;
            }

            if (m_TranscriptSessionId != lobby.sessionId)
            {
                ResetTranscript();
                m_TranscriptSessionId = lobby.sessionId;
            }

            m_TranscriptText.gameObject.SetActive(true);
            if (m_IsPolling || Time.unscaledTime < m_NextTranscriptPoll)
            {
                return;
            }

            m_IsPolling = true;
            m_NextTranscriptPoll = Time.unscaledTime + (m_TranscriptPushActive ? k_TranscriptPushPollInterval : k_TranscriptPollInterval);
            try
            {
                var messages = await m_GatheringsFacade.GetTranscriptAsync(m_LastTranscriptTimestamp);
                if (this == null || m_TranscriptSessionId != lobby.sessionId)
                {
                    return;
                }

                foreach (var message in messages)
                {
                    AddTranscriptLine(message);
                    AdvanceTranscriptCursor(message.timestamp);
                }

                RenderTranscript();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to fetch transcript: {e.Message}");
            }
            finally
            {
                m_IsPolling = false;
            }
        }

        void AddTranscriptLine(TranscriptMessage message)
        {
            var line = $"<b>{message.senderName}:</b> {message.content}";
            if (message.flagged)
            {
                var categories = message.categories != null && message.categories.Length > 0 ? string.Join(", ", message.categories) : "moderation";
                line = $"<color=#ff6666>{line} <size=70%>[flagged: {categories}]</size></color>";
            }

            // a line that was pushed already is not shown twice
            m_TranscriptLines.Upsert(message.id, line, replace: false);
        }

        /// <summary>
        /// Shows a transcript line the Cortex bot pushed into the room (frame <c>cortex.transcript</c>).
        /// </summary>
        /// <param name="segmentId">The segment; the Cortex message id, reused by interim updates.</param>
        /// <param name="peerId">ODIN peer of the speaker.</param>
        /// <param name="text">The transcribed text.</param>
        /// <param name="interim">True for a provisional caption that a later frame replaces.</param>
        /// <param name="timestamp">ISO 8601 time of the segment, used to advance the polling cursor.</param>
        public void AddPushedTranscript(string segmentId, uint peerId, string text, bool interim, string timestamp)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            m_TranscriptPushActive = true;
            var transport = m_NetworkManager != null ? m_NetworkManager.NetworkConfig.NetworkTransport as OdinNetcodeTransport : null;
            var name = transport != null && transport.TryGetPeerName(peerId, out var peerName) ? peerName : "Player";
            var line = interim ? $"<b>{name}:</b> <i>{text}</i>" : $"<b>{name}:</b> {text}";

            // an interim caption is replaced in place by its next update or its final text
            if (m_TranscriptLines.Upsert(segmentId, line, replace: true))
            {
                RenderTranscript();
            }

            if (!interim)
            {
                AdvanceTranscriptCursor(timestamp);
            }
        }

        /// <summary>
        /// Polls once right away, e.g. after the listener noticed missed frames or the bot rejoined the room.
        /// </summary>
        public void RequestTranscriptCatchUp()
        {
            m_NextTranscriptPoll = 0;
        }

        /// <summary>
        /// Moves the polling cursor forward (the backend returns messages newer than it). Pushed and polled lines
        /// can arrive out of order, so the cursor never moves back.
        /// </summary>
        void AdvanceTranscriptCursor(string timestamp)
        {
            if (!DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var next))
            {
                return;
            }

            if (string.IsNullOrEmpty(m_LastTranscriptTimestamp) ||
                !DateTime.TryParse(m_LastTranscriptTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var current) ||
                next > current)
            {
                m_LastTranscriptTimestamp = timestamp;
            }
        }

        void RenderTranscript()
        {
            m_TranscriptText.SetText(m_TranscriptLines.Count == 0
                ? "<size=80%><i>Voice transcript will appear here</i></size>"
                : string.Join("\n", m_TranscriptLines.Lines));
        }

        void ResetTranscript()
        {
            m_TranscriptLines.Clear();
            m_LastTranscriptTimestamp = null;
            m_TranscriptSessionId = null;
            m_TranscriptPushActive = false;
            if (m_TranscriptText != null)
            {
                m_TranscriptText.SetText(string.Empty);
            }
        }

        void BuildUI()
        {
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            m_StatusText = CreateText("Voice Status", new Vector2(0, 0), new Vector2(24, 24), TextAlignmentOptions.BottomLeft, 24);
            m_TranscriptText = CreateText("Voice Transcript", new Vector2(1, 0), new Vector2(-24, 24), TextAlignmentOptions.BottomRight, 22);
            m_TranscriptText.gameObject.SetActive(false);
            m_NoticeText = CreateText("Moderation Notice", new Vector2(0.5f, 1), new Vector2(0, -24), TextAlignmentOptions.Top, 26);
            m_NoticeText.gameObject.SetActive(false);
            m_Canvas.enabled = false;
        }

        TextMeshProUGUI CreateText(string objectName, Vector2 anchor, Vector2 offset, TextAlignmentOptions alignment, float fontSize)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(transform, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(720, 260);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.richText = true;
            text.raycastTarget = false;
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
            return text;
        }
    }
}
