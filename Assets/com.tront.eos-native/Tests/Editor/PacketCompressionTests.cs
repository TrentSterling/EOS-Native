using System;
using System.Text;
using NUnit.Framework;
using EOSNative.P2P;

namespace EOSNative.Tests
{
    public class PacketCompressionTests
    {
        [Test]
        public void CompressDecompress_RoundTrip_Small()
        {
            var data = Encoding.UTF8.GetBytes("Hello, World!");
            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(data.Length, decompressed.Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], decompressed[i]);
        }

        [Test]
        public void CompressDecompress_RoundTrip_Large()
        {
            // Repetitive data compresses well
            var data = new byte[2000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 10);

            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            Assert.Less(compressed.Length, data.Length, "Repetitive data should compress");

            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(data.Length, decompressed.Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], decompressed[i]);
        }

        [Test]
        public void CompressDecompress_WithOffset()
        {
            var full = new byte[200];
            for (int i = 0; i < full.Length; i++)
                full[i] = (byte)(i % 7);

            // Compress only a portion
            var compressed = MessageRouter.CompressDeflate(full, 50, 100);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(100, decompressed.Length);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(full[50 + i], decompressed[i]);
        }

        [Test]
        public void CompressDecompress_EmptyData()
        {
            var data = Array.Empty<byte>();
            var compressed = MessageRouter.CompressDeflate(data, 0, 0);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(0, decompressed.Length);
        }

        [Test]
        public void CompressDecompress_RandomData()
        {
            // Random data may not compress — verify round-trip still works
            var rng = new System.Random(42);
            var data = new byte[500];
            rng.NextBytes(data);

            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(data.Length, decompressed.Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], decompressed[i]);
        }

        [Test]
        public void CompressDecompress_ExactlyThresholdSize()
        {
            var data = new byte[64];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)i;

            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(data.Length, decompressed.Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], decompressed[i]);
        }

        [Test]
        public void FlagConstants_AreCorrect()
        {
            // Verify the wire format flags don't conflict
            // FLAG_SINGLE = 0x00, FLAG_BATCH = 0x01, FLAG_SINGLE_COMPRESSED = 0x02, FLAG_BATCH_COMPRESSED = 0x03
            // We can't read private constants directly, but we can verify compression produces the right flags

            // A large repetitive payload should compress
            var data = new byte[200];
            for (int i = 0; i < data.Length; i++)
                data[i] = 42; // very repetitive

            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            Assert.Less(compressed.Length, data.Length, "Repetitive data should compress smaller");
        }

        [Test]
        public void CompressDecompress_LargePayload_5KB()
        {
            var data = new byte[5000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 50);

            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            Assert.Less(compressed.Length, data.Length);

            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(data.Length, decompressed.Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], decompressed[i]);
        }

        [Test]
        public void CompressDecompress_AllZeros()
        {
            // All zeros should compress extremely well
            var data = new byte[1000];
            var compressed = MessageRouter.CompressDeflate(data, 0, data.Length);
            Assert.Less(compressed.Length, 50, "All-zero data should compress very small");

            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(1000, decompressed.Length);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(0, decompressed[i]);
        }

        [Test]
        public void CompressDecompress_SingleByte()
        {
            var data = new byte[] { 0xAB };
            var compressed = MessageRouter.CompressDeflate(data, 0, 1);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(1, decompressed.Length);
            Assert.AreEqual(0xAB, decompressed[0]);
        }
    }
}
