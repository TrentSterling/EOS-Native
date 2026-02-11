using System;
using System.Collections.Generic;
using NUnit.Framework;
using EOSNative.P2P;

namespace EOSNative.Tests
{
    public class PacketFragmenterTests
    {
        [Test]
        public void Constants_Correct()
        {
            Assert.AreEqual(7, PacketFragmenter.HeaderSize);
            Assert.AreEqual(1163, PacketFragmenter.MaxPayloadSize);
            Assert.AreEqual(1170, PacketFragmenter.MaxPacketSize);
        }

        [Test]
        public void NeedsFragmentation_SmallPayload()
        {
            Assert.IsFalse(PacketFragmenter.NeedsFragmentation(100));
            Assert.IsFalse(PacketFragmenter.NeedsFragmentation(1163));
        }

        [Test]
        public void NeedsFragmentation_LargePayload()
        {
            Assert.IsTrue(PacketFragmenter.NeedsFragmentation(1164));
            Assert.IsTrue(PacketFragmenter.NeedsFragmentation(5000));
        }

        [Test]
        public void SingleFragment_FastPath()
        {
            var fragmenter = new PacketFragmenter();
            var data = new byte[100];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            Assert.AreEqual(1, fragments.Count);
            Assert.AreEqual(100 + PacketFragmenter.HeaderSize, fragments[0].Count);

            // Reassemble
            var result = fragmenter.ProcessIncoming(null, fragments[0], 0);
            Assert.IsNotNull(result);
            Assert.AreEqual(100, result.Length);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual((byte)(i % 256), result[i]);
        }

        [Test]
        public void MultiFragment_RoundTrip()
        {
            var fragmenter = new PacketFragmenter();
            int dataSize = 3000; // Should produce 3 fragments
            var data = new byte[dataSize];
            for (int i = 0; i < dataSize; i++) data[i] = (byte)(i % 256);

            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            Assert.AreEqual(3, fragments.Count); // 1163 + 1163 + 674

            // Process fragments in order
            byte[] result = null;
            for (int i = 0; i < fragments.Count; i++)
            {
                result = fragmenter.ProcessIncoming(null, fragments[i], 0);
                if (i < fragments.Count - 1)
                    Assert.IsNull(result, $"Should be null for fragment {i}");
            }

            Assert.IsNotNull(result);
            Assert.AreEqual(dataSize, result.Length);
            for (int i = 0; i < dataSize; i++)
                Assert.AreEqual((byte)(i % 256), result[i], $"Mismatch at byte {i}");
        }

        [Test]
        public void MultiFragment_OutOfOrder()
        {
            var fragmenter = new PacketFragmenter();
            int dataSize = 2500;
            var data = new byte[dataSize];
            for (int i = 0; i < dataSize; i++) data[i] = (byte)(i % 256);

            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            Assert.AreEqual(3, fragments.Count);

            // Process out of order: 2, 0, 1
            Assert.IsNull(fragmenter.ProcessIncoming(null, fragments[2], 0));
            Assert.IsNull(fragmenter.ProcessIncoming(null, fragments[0], 0));
            var result = fragmenter.ProcessIncoming(null, fragments[1], 0);

            Assert.IsNotNull(result);
            Assert.AreEqual(dataSize, result.Length);
        }

        [Test]
        public void FragmentCount_ExactMultiple()
        {
            // 2326 bytes = exactly 2 full fragments
            var fragmenter = new PacketFragmenter();
            var data = new byte[2326];
            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);
            Assert.AreEqual(2, fragments.Count);
        }

        [Test]
        public void FragmentCount_JustOver()
        {
            // 1164 bytes = 2 fragments (1163 + 1)
            var fragmenter = new PacketFragmenter();
            var data = new byte[1164];
            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);
            Assert.AreEqual(2, fragments.Count);
        }

        [Test]
        public void EmptyPayload()
        {
            var fragmenter = new PacketFragmenter();
            var data = new byte[0];
            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            // Should still produce a fragment (header only, marking last)
            Assert.AreEqual(0, fragments.Count);
        }

        [Test]
        public void TooShortPacket_ReturnsNull()
        {
            var fragmenter = new PacketFragmenter();
            var tooShort = new byte[3]; // Less than header size
            var result = fragmenter.ProcessIncoming(null, new ArraySegment<byte>(tooShort), 0);
            Assert.IsNull(result);
        }

        [Test]
        public void ClearAll_RemovesPending()
        {
            var fragmenter = new PacketFragmenter();
            var data = new byte[2500];
            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            // Process only first fragment
            fragmenter.ProcessIncoming(null, fragments[0], 0);

            // Clear and verify next fragments don't reassemble
            fragmenter.ClearAll();

            // Processing remaining fragments should not produce a complete packet
            // because the assembly was cleared
            var result = fragmenter.ProcessIncoming(null, fragments[1], 0);
            Assert.IsNull(result); // First fragment was cleared
        }

        [Test]
        public void StaleTimeout_DefaultIs5()
        {
            var fragmenter = new PacketFragmenter();
            Assert.AreEqual(5f, fragmenter.StaleTimeout);
        }

        [Test]
        public void StaleTimeout_IsConfigurable()
        {
            var fragmenter = new PacketFragmenter();
            fragmenter.StaleTimeout = 0.5f;
            Assert.AreEqual(0.5f, fragmenter.StaleTimeout);
        }

        [Test]
        public void MaxPayloadSize_SingleFragment()
        {
            var fragmenter = new PacketFragmenter();
            var data = new byte[PacketFragmenter.MaxPayloadSize]; // Exactly max payload
            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);
            Assert.AreEqual(1, fragments.Count);

            var result = fragmenter.ProcessIncoming(null, fragments[0], 0);
            Assert.IsNotNull(result);
            Assert.AreEqual(PacketFragmenter.MaxPayloadSize, result.Length);
        }

        [Test]
        public void DuplicateFragment_Ignored()
        {
            var fragmenter = new PacketFragmenter();
            var data = new byte[2500];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

            var fragments = new List<ArraySegment<byte>>();
            fragmenter.Fragment(new ArraySegment<byte>(data), fragments);

            // Process fragment 0 twice
            Assert.IsNull(fragmenter.ProcessIncoming(null, fragments[0], 0));
            Assert.IsNull(fragmenter.ProcessIncoming(null, fragments[0], 0)); // duplicate

            // Process remaining
            Assert.IsNull(fragmenter.ProcessIncoming(null, fragments[1], 0));
            var result = fragmenter.ProcessIncoming(null, fragments[2], 0);
            Assert.IsNotNull(result);
            Assert.AreEqual(2500, result.Length);
        }
    }
}
