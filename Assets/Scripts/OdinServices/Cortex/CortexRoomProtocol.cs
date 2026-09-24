using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.BossRoom.OdinServices.Cortex
{
    /// <summary>
    /// Parses the frames the ODIN Cortex transcription bot sends into the voice room (the Cortex Room Protocol).
    /// </summary>
    /// <remarks>
    /// Cortex enforces sanctions in the room through the bot: with the project setting <c>sanctionPush</c> it
    /// broadcasts who is muted, and every client stops receiving those peers' audio. The bot sends plain ODIN
    /// messages, which it shares with whatever else the game sends, so a Cortex frame is recognised by two things:
    /// it comes from the bot peer (checked by the caller) and it starts with a small binary header.
    ///
    /// Frame layout: bytes 0-1 are the magic <c>0x43 0x58</c> ("CX"), byte 2 is the protocol version (1), and the
    /// rest is UTF-8 JSON <c>{ "type", "id", "seq", "data" }</c>. Anything else is not a Cortex frame and is ignored.
    ///
    /// This class has no Unity or ODIN dependencies beyond <see cref="JsonUtility"/>, so it is unit tested directly.
    /// </remarks>
    public static class CortexRoomProtocol
    {
        /// <summary>First magic byte, ASCII 'C'.</summary>
        public const byte k_Magic0 = 0x43;
        /// <summary>Second magic byte, ASCII 'X'.</summary>
        public const byte k_Magic1 = 0x58;
        /// <summary>The only protocol version this client understands.</summary>
        public const byte k_Version = 1;
        /// <summary>Header length: two magic bytes and the version.</summary>
        public const int k_HeaderLength = 3;

        /// <summary>A transcribed segment of one speaker.</summary>
        public const string k_TypeTranscript = "cortex.transcript";
        /// <summary>A sanction of a player in the room changed.</summary>
        public const string k_TypeSanction = "cortex.sanction";
        /// <summary>The complete set of muted peers, sent to every peer that joins.</summary>
        public const string k_TypeMutes = "cortex.mutes";

        /// <summary>ODIN tag Cortex signs into the bot's token; players can never get it.</summary>
        public const string k_BotTag = "cortex:bot";
        /// <summary>ODIN tag the Cortex join gate puts on the token of a muted player.</summary>
        public const string k_MutedTag = "cortex:muted";
        /// <summary>
        /// User id of the bot when the project's token provider cannot sign tags (the <c>app_settings.botUserId</c> default).
        /// </summary>
        public const string k_DefaultBotUserId = "transcription-bot";

        /// <summary>
        /// Parses a message into a Cortex frame.
        /// </summary>
        /// <param name="bytes">The raw ODIN message.</param>
        /// <param name="frame">The frame, or null if the message is not a Cortex frame.</param>
        /// <returns>True for a well-formed frame of a supported version.</returns>
        public static bool TryParse(byte[] bytes, out CortexFrame frame)
        {
            frame = null;
            // an empty JSON object is the shortest valid body, so anything shorter is not a frame
            if (bytes == null || bytes.Length < k_HeaderLength + 2 || bytes[0] != k_Magic0 || bytes[1] != k_Magic1 || bytes[2] != k_Version)
            {
                return false;
            }

            try
            {
                var json = Encoding.UTF8.GetString(bytes, k_HeaderLength, bytes.Length - k_HeaderLength);
                frame = JsonUtility.FromJson<CortexFrame>(json);
            }
            catch (Exception)
            {
                // a corrupt frame is dropped like any other unknown message; there is nobody to report it to
                frame = null;
            }

            if (frame == null || string.IsNullOrEmpty(frame.type))
            {
                frame = null;
                return false;
            }

            frame.data ??= new CortexFrameData();
            return true;
        }

        /// <summary>
        /// Builds a frame, as the bot would send it. Used by tests and handy for simulating the bot locally.
        /// </summary>
        /// <param name="frame">The frame to encode.</param>
        public static byte[] Encode(CortexFrame frame)
        {
            var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame));
            var bytes = new byte[k_HeaderLength + json.Length];
            bytes[0] = k_Magic0;
            bytes[1] = k_Magic1;
            bytes[2] = k_Version;
            Buffer.BlockCopy(json, 0, bytes, k_HeaderLength, json.Length);
            return bytes;
        }

        /// <summary>
        /// Whether a sanction type takes a player's voice away (the ones the bot enforces with a mute).
        /// </summary>
        public static bool IsVoiceMute(string sanctionType) =>
            sanctionType == "mute" || sanctionType == "listen_only" || sanctionType == "text_only" || sanctionType == "shadow_mute";

        /// <summary>
        /// Whether a sanction type bans the player from the game.
        /// </summary>
        public static bool IsBan(string sanctionType) => sanctionType == "temp_ban" || sanctionType == "perm_ban";
    }

    /// <summary>
    /// One Cortex frame. <see cref="data"/> holds the fields of every frame type; a type only fills its own.
    /// </summary>
    [Serializable]
    public class CortexFrame
    {
        /// <summary>Frame type, e.g. <c>cortex.sanction</c>. Types starting with <c>cortex.</c> can only come from Cortex.</summary>
        public string type;
        /// <summary>Unique frame id.</summary>
        public string id;
        /// <summary>Frame counter of the bot connection: starts at 1 when the bot joins, a gap means a missed frame.</summary>
        public long seq;
        /// <summary>The payload.</summary>
        public CortexFrameData data;
    }

    /// <summary>
    /// Union of the payloads of the Cortex frame types. JsonUtility cannot parse into a generic object, so the
    /// fields of all types live side by side and the ones a frame does not carry keep their default.
    /// </summary>
    [Serializable]
    public class CortexFrameData
    {
        // cortex.sanction

        /// <summary><c>created</c>, <c>updated</c>, <c>revoked</c>, <c>expired</c> or <c>active</c>.</summary>
        public string action;
        /// <summary>The sanction the frame is about.</summary>
        public CortexSanction sanction;
        /// <summary>The sanctioned player's peers in this room.</summary>
        public uint[] peerIds;
        /// <summary>True: stop hearing <see cref="peerIds"/>. False: hear them again (or: the frame is only a notice).</summary>
        public bool mute;

        // cortex.mutes

        /// <summary>Every peer that must not be heard. Replaces whatever the client held before.</summary>
        public CortexMute[] mutes;

        // cortex.transcript

        /// <summary>The segment; equals the message id, and interim updates of one segment reuse it.</summary>
        public string segmentId;
        /// <summary>ODIN peer of the speaker: attach the line to that peer.</summary>
        public uint peerId;
        /// <summary>Transcribed text.</summary>
        public string text;
        /// <summary>Language detected by the speech-to-text provider, empty if unknown.</summary>
        public string lang;
        /// <summary>True for a provisional caption that a later frame with the same segment replaces.</summary>
        public bool interim;
        /// <summary>ISO 8601 time of the segment; the backend's transcript cursor compares against it.</summary>
        public string ts;
    }

    /// <summary>
    /// The latest transcript lines, filled from pushed <c>cortex.transcript</c> frames and from polling alike.
    /// </summary>
    /// <remarks>
    /// Push and polling overlap on purpose: push shows a line within the speech-to-text latency, and a slow poll
    /// still catches what push missed (a frame gap, a bot rejoin, or other players' lines when the project pushes
    /// only to the speaker). Both sources use the Cortex message id (a frame's <c>segmentId</c> is the message id),
    /// so a line that arrives twice is stored once, and an update of a segment replaces its line in place.
    /// </remarks>
    public class CortexTranscriptBuffer
    {
        readonly int m_MaxLines;
        readonly List<KeyValuePair<string, string>> m_Lines = new List<KeyValuePair<string, string>>();
        readonly HashSet<string> m_Seen = new HashSet<string>();

        /// <summary>Creates a buffer that keeps the newest <paramref name="maxLines"/> lines.</summary>
        public CortexTranscriptBuffer(int maxLines)
        {
            m_MaxLines = maxLines;
        }

        /// <summary>The kept lines, oldest first.</summary>
        public IEnumerable<string> Lines
        {
            get
            {
                foreach (var line in m_Lines)
                {
                    yield return line.Value;
                }
            }
        }

        /// <summary>Number of kept lines.</summary>
        public int Count => m_Lines.Count;

        /// <summary>
        /// Adds a line, or replaces the line of the same message.
        /// </summary>
        /// <param name="id">Cortex message id (the segment id of a pushed frame).</param>
        /// <param name="line">The rendered line.</param>
        /// <param name="replace">True to update a line that is already there (an interim caption becoming final);
        /// false to ignore a message that was already shown (polling returning a pushed line).</param>
        /// <returns>True if the visible lines changed.</returns>
        public bool Upsert(string id, string line, bool replace)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (m_Seen.Contains(id))
            {
                if (!replace)
                {
                    return false;
                }

                for (var i = 0; i < m_Lines.Count; i++)
                {
                    if (m_Lines[i].Key == id)
                    {
                        m_Lines[i] = new KeyValuePair<string, string>(id, line);
                        return true;
                    }
                }

                // seen but already scrolled out: an old segment is not brought back
                return false;
            }

            m_Seen.Add(id);
            m_Lines.Add(new KeyValuePair<string, string>(id, line));
            while (m_Lines.Count > m_MaxLines)
            {
                m_Lines.RemoveAt(0);
            }

            return true;
        }

        /// <summary>Forgets everything, e.g. when the session changes.</summary>
        public void Clear()
        {
            m_Lines.Clear();
            m_Seen.Clear();
        }
    }

    /// <summary>
    /// A sanction as it appears in a <c>cortex.sanction</c> frame.
    /// </summary>
    [Serializable]
    public class CortexSanction
    {
        /// <summary>Sanction id.</summary>
        public string id;
        /// <summary>e.g. <c>warn</c>, <c>mute</c>, <c>temp_ban</c>.</summary>
        public string type;
        /// <summary>The sanctioned Cortex participant.</summary>
        public string participantId;
        /// <summary>The sanctioned player's id in the game.</summary>
        public string externalUserId;
        /// <summary>Why it was issued, e.g. "Auto-sanctioned (level 2): …".</summary>
        public string reason;
        /// <summary>ISO 8601 start.</summary>
        public string startAt;
        /// <summary>ISO 8601 end; empty for a permanent sanction.</summary>
        public string endAt;
        /// <summary><c>active</c>, <c>revoked</c>, …</summary>
        public string status;
    }

    /// <summary>
    /// One entry of a <c>cortex.mutes</c> frame.
    /// </summary>
    [Serializable]
    public class CortexMute
    {
        /// <summary>The muted peer.</summary>
        public uint peerId;
        /// <summary>The muted Cortex participant.</summary>
        public string participantId;
        /// <summary>The muted player's id in the game.</summary>
        public string externalUserId;
        /// <summary>The sanction behind the mute.</summary>
        public string sanctionId;
        /// <summary>The sanction type, e.g. <c>mute</c>.</summary>
        public string type;
        /// <summary>ISO 8601 end of the mute; empty if permanent.</summary>
        public string endAt;
    }

    /// <summary>
    /// The set of remote peers this client must not hear, maintained from Cortex frames and peer tags.
    /// </summary>
    /// <remarks>
    /// Rules from the Room Protocol: <c>cortex.mutes</c> replaces the set; <c>cortex.sanction</c> with
    /// <c>mute: true</c> adds its peers and with <c>mute: false</c> removes them; a peer that joins with the tag
    /// <c>cortex:muted</c> is added right away, before the bot has said anything. The local peer is never part of
    /// the set: not hearing yourself changes nothing, and the server already stops others from hearing you.
    ///
    /// Each change reports which peers were added and removed, so the caller applies exactly those as ODIN
    /// channel masks.
    /// </remarks>
    public class CortexMuteSet
    {
        readonly HashSet<uint> m_Muted = new HashSet<uint>();

        /// <summary>The local peer, which is never muted locally. 0 until the room is joined.</summary>
        public uint LocalPeerId { get; set; }

        /// <summary>The peers currently muted.</summary>
        public IReadOnlyCollection<uint> Muted => m_Muted;

        /// <summary>Whether a peer is muted.</summary>
        public bool Contains(uint peerId) => m_Muted.Contains(peerId);

        /// <summary>
        /// Replaces the set, as a <c>cortex.mutes</c> frame asks.
        /// </summary>
        /// <param name="peerIds">The complete list of muted peers.</param>
        /// <param name="added">Peers that are muted now and were not before.</param>
        /// <param name="removed">Peers that were muted and are not any more.</param>
        public void Replace(IEnumerable<uint> peerIds, List<uint> added, List<uint> removed)
        {
            var next = new HashSet<uint>();
            if (peerIds != null)
            {
                foreach (var peerId in peerIds)
                {
                    if (IsRemote(peerId))
                    {
                        next.Add(peerId);
                    }
                }
            }

            foreach (var peerId in m_Muted)
            {
                if (!next.Contains(peerId))
                {
                    removed.Add(peerId);
                }
            }

            foreach (var peerId in next)
            {
                if (!m_Muted.Contains(peerId))
                {
                    added.Add(peerId);
                }
            }

            m_Muted.Clear();
            m_Muted.UnionWith(next);
        }

        /// <summary>
        /// Mutes or unmutes peers, as a <c>cortex.sanction</c> frame or a <c>cortex:muted</c> tag asks.
        /// </summary>
        /// <param name="peerIds">The peers the change is about.</param>
        /// <param name="mute">True to mute, false to unmute.</param>
        /// <param name="changed">Peers whose state actually changed.</param>
        public void Set(IEnumerable<uint> peerIds, bool mute, List<uint> changed)
        {
            if (peerIds == null)
            {
                return;
            }

            foreach (var peerId in peerIds)
            {
                if (!IsRemote(peerId))
                {
                    continue;
                }

                if (mute ? m_Muted.Add(peerId) : m_Muted.Remove(peerId))
                {
                    changed.Add(peerId);
                }
            }
        }

        /// <summary>
        /// Forgets a peer that left the room (ODIN drops its channel mask by itself).
        /// </summary>
        public void Forget(uint peerId) => m_Muted.Remove(peerId);

        /// <summary>
        /// Forgets everything, e.g. when the room is left.
        /// </summary>
        public void Clear()
        {
            m_Muted.Clear();
            LocalPeerId = 0;
        }

        bool IsRemote(uint peerId) => peerId != 0 && peerId != LocalPeerId;
    }
}
