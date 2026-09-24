using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.BossRoom.OdinServices.Cortex;

namespace Unity.BossRoom.Tests.Runtime
{
    /// <summary>
    /// The parts of the Cortex Room Protocol that decide what a client mutes: recognising a Cortex frame, and the
    /// mute set rules from the protocol docs (cortex.mutes replaces, cortex.sanction adds or removes, the local
    /// peer is never muted). Pure C#: no room, no network.
    /// </summary>
    public class CortexRoomProtocolTests
    {
        static byte[] Frame(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var bytes = new byte[3 + body.Length];
            bytes[0] = 0x43;
            bytes[1] = 0x58;
            bytes[2] = 1;
            body.CopyTo(bytes, 3);
            return bytes;
        }

        [Test]
        public void ParsesASanctionFrameAsTheBotSendsIt()
        {
            var bytes = Frame("{\"type\":\"cortex.sanction\",\"id\":\"f1\",\"seq\":7,\"data\":{\"action\":\"created\"," +
                              "\"sanction\":{\"id\":\"s1\",\"type\":\"mute\",\"participantId\":\"p1\",\"externalUserId\":\"alice\"," +
                              "\"reason\":\"Auto-sanctioned\",\"startAt\":\"2026-09-23T10:00:00.000Z\",\"endAt\":\"2026-09-23T10:05:00.000Z\",\"status\":\"active\"}," +
                              "\"peerIds\":[17,18],\"mute\":true}}");

            Assert.IsTrue(CortexRoomProtocol.TryParse(bytes, out var frame));
            Assert.AreEqual(CortexRoomProtocol.k_TypeSanction, frame.type);
            Assert.AreEqual(7, frame.seq);
            Assert.AreEqual("created", frame.data.action);
            Assert.AreEqual("mute", frame.data.sanction.type);
            Assert.AreEqual("2026-09-23T10:05:00.000Z", frame.data.sanction.endAt);
            CollectionAssert.AreEqual(new uint[] { 17, 18 }, frame.data.peerIds);
            Assert.IsTrue(frame.data.mute);
        }

        [Test]
        public void ParsesAMutesFrame()
        {
            var bytes = Frame("{\"type\":\"cortex.mutes\",\"id\":\"f2\",\"seq\":1,\"data\":{\"mutes\":[" +
                              "{\"peerId\":17,\"participantId\":\"p1\",\"externalUserId\":\"alice\",\"sanctionId\":\"s1\",\"type\":\"mute\",\"endAt\":\"2026-09-23T11:25:27.976Z\"}]}}");

            Assert.IsTrue(CortexRoomProtocol.TryParse(bytes, out var frame));
            Assert.AreEqual(1, frame.data.mutes.Length);
            Assert.AreEqual(17u, frame.data.mutes[0].peerId);
        }

        [Test]
        public void RoundTripsThroughEncode()
        {
            var sent = new CortexFrame { type = "cortex.sanction", id = "f3", seq = 2, data = new CortexFrameData { peerIds = new uint[] { 5 }, mute = true } };

            Assert.IsTrue(CortexRoomProtocol.TryParse(CortexRoomProtocol.Encode(sent), out var received));
            Assert.AreEqual("f3", received.id);
            CollectionAssert.AreEqual(new uint[] { 5 }, received.data.peerIds);
        }

        [Test]
        public void IgnoresMessagesThatAreNotCortexFrames()
        {
            // the game's own messages, a future protocol version, a truncated header and garbage JSON
            Assert.IsFalse(CortexRoomProtocol.TryParse(Encoding.UTF8.GetBytes("{\"type\":\"cortex.mutes\"}"), out _));
            var future = Frame("{\"type\":\"cortex.mutes\"}");
            future[2] = 2;
            Assert.IsFalse(CortexRoomProtocol.TryParse(future, out _));
            Assert.IsFalse(CortexRoomProtocol.TryParse(new byte[] { 0x43, 0x58 }, out _));
            Assert.IsFalse(CortexRoomProtocol.TryParse(Frame("not json"), out _));
            Assert.IsFalse(CortexRoomProtocol.TryParse(Frame("{\"id\":\"no type\"}"), out _));
            Assert.IsFalse(CortexRoomProtocol.TryParse(null, out _));
        }

        [Test]
        public void MutesReplaceTheSetAndReportTheDifference()
        {
            var set = new CortexMuteSet { LocalPeerId = 1 };
            var added = new List<uint>();
            var removed = new List<uint>();

            set.Replace(new uint[] { 17, 18 }, added, removed);
            CollectionAssert.AreEquivalent(new uint[] { 17, 18 }, added);
            CollectionAssert.IsEmpty(removed);

            added.Clear();
            set.Replace(new uint[] { 18, 19 }, added, removed);
            CollectionAssert.AreEquivalent(new uint[] { 19 }, added);
            CollectionAssert.AreEquivalent(new uint[] { 17 }, removed);
            CollectionAssert.AreEquivalent(new uint[] { 18, 19 }, set.Muted);
        }

        [Test]
        public void SanctionFramesAddAndRemovePeersOnlyOnce()
        {
            var set = new CortexMuteSet { LocalPeerId = 1 };
            var changed = new List<uint>();

            set.Set(new uint[] { 17 }, true, changed);
            set.Set(new uint[] { 17 }, true, changed);
            CollectionAssert.AreEqual(new uint[] { 17 }, changed, "a repeated mute changes nothing");

            changed.Clear();
            set.Set(new uint[] { 17, 20 }, false, changed);
            CollectionAssert.AreEqual(new uint[] { 17 }, changed, "only muted peers are unmuted");
            Assert.IsFalse(set.Contains(17));
        }

        [Test]
        public void NeverMutesTheLocalPeer()
        {
            var set = new CortexMuteSet { LocalPeerId = 1 };
            var changed = new List<uint>();
            var removed = new List<uint>();

            set.Set(new uint[] { 1, 0 }, true, changed);
            set.Replace(new uint[] { 1 }, changed, removed);

            CollectionAssert.IsEmpty(changed);
            CollectionAssert.IsEmpty(set.Muted);
        }

        [Test]
        public void ForgetsPeersThatLeft()
        {
            var set = new CortexMuteSet { LocalPeerId = 1 };
            set.Set(new uint[] { 17 }, true, new List<uint>());

            set.Forget(17);

            Assert.IsFalse(set.Contains(17));
        }

        [Test]
        public void ClassifiesSanctionTypes()
        {
            Assert.IsTrue(CortexRoomProtocol.IsVoiceMute("mute"));
            Assert.IsTrue(CortexRoomProtocol.IsVoiceMute("shadow_mute"));
            Assert.IsFalse(CortexRoomProtocol.IsVoiceMute("warn"));
            Assert.IsTrue(CortexRoomProtocol.IsBan("temp_ban"));
            Assert.IsTrue(CortexRoomProtocol.IsBan("perm_ban"));
            Assert.IsFalse(CortexRoomProtocol.IsBan("mute"));
        }
    }
}
