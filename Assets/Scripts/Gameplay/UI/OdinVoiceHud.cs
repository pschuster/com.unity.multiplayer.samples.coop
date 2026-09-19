using System;
using System.Collections.Generic;
using System.Text;
using OdinNative.Netcode;
using OdinNative.Netcode.Voice;
using TMPro;
using Unity.BossRoom.OdinServices.Backend;
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
    /// </summary>
    public class OdinVoiceHud : MonoBehaviour
    {
        const float k_TranscriptPollInterval = 2f;
        const int k_MaxTranscriptLines = 6;
        const Key k_MuteKey = Key.M;
        const Key k_PartyRadioKey = Key.V;

        [Inject]
        GatheringsFacade m_GatheringsFacade;
        [Inject]
        NetworkManager m_NetworkManager;

        readonly Queue<string> m_TranscriptLines = new Queue<string>();
        readonly HashSet<uint> m_TalkingPeers = new HashSet<uint>();
        readonly StringBuilder m_Text = new StringBuilder();

        Canvas m_Canvas;
        TextMeshProUGUI m_StatusText;
        TextMeshProUGUI m_TranscriptText;
        OdinNetcodeVoice m_Voice;
        string m_LastTranscriptTimestamp;
        string m_TranscriptSessionId;
        float m_NextTranscriptPoll;
        bool m_IsPolling;

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
                return;
            }

            HandleInput();
            UpdateStatus();
            PollTranscript();
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
            m_NextTranscriptPoll = Time.unscaledTime + k_TranscriptPollInterval;
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
                    m_LastTranscriptTimestamp = message.timestamp;
                }

                m_TranscriptText.SetText(m_TranscriptLines.Count == 0
                    ? "<size=80%><i>Voice transcript will appear here</i></size>"
                    : string.Join("\n", m_TranscriptLines));
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

            m_TranscriptLines.Enqueue(line);
            while (m_TranscriptLines.Count > k_MaxTranscriptLines)
            {
                m_TranscriptLines.Dequeue();
            }
        }

        void ResetTranscript()
        {
            m_TranscriptLines.Clear();
            m_LastTranscriptTimestamp = null;
            m_TranscriptSessionId = null;
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
