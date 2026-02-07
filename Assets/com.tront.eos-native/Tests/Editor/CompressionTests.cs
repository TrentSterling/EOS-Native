using NUnit.Framework;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class CompressionTests
    {
        #region Vector3Half

        [Test]
        public void Vector3Half_Zero()
        {
            var half = new Vector3Half(Vector3.zero);
            var result = half.ToVector3();
            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(0f, result.z, 0.001f);
        }

        [Test]
        public void Vector3Half_Positive()
        {
            var original = new Vector3(1f, 2f, 3f);
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            Assert.AreEqual(1f, result.x, 0.01f);
            Assert.AreEqual(2f, result.y, 0.01f);
            Assert.AreEqual(3f, result.z, 0.01f);
        }

        [Test]
        public void Vector3Half_Negative()
        {
            var original = new Vector3(-5f, -10f, -15f);
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            Assert.AreEqual(-5f, result.x, 0.01f);
            Assert.AreEqual(-10f, result.y, 0.01f);
            Assert.AreEqual(-15f, result.z, 0.01f);
        }

        [Test]
        public void Vector3Half_SmallValues()
        {
            var original = new Vector3(0.1f, 0.01f, 0.001f);
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            // Half precision has limited accuracy for small values
            Assert.AreEqual(0.1f, result.x, 0.01f);
            Assert.AreEqual(0.01f, result.y, 0.001f);
        }

        [Test]
        public void Vector3Half_WriteRead_RoundTrip()
        {
            var original = new Vector3(42.5f, -17.25f, 100f);
            var w = new NetWriter();
            w.WriteVector3Half(original);
            Assert.AreEqual(6, w.Position, "Vector3Half should be 6 bytes");

            var r = new NetReader(w.ToArray());
            var result = r.ReadVector3Half();
            Assert.AreEqual(42.5f, result.x, 0.1f);
            Assert.AreEqual(-17.25f, result.y, 0.1f);
            Assert.AreEqual(100f, result.z, 0.1f);
        }

        [Test]
        public void Vector3Half_Size_Is6Bytes()
        {
            // 3 x ushort = 6 bytes
            var w = new NetWriter();
            w.WriteVector3Half(new Vector3(1, 2, 3));
            Assert.AreEqual(6, w.Position);
        }

        #endregion

        #region Compressed Rotation

        [Test]
        public void CompressedRotation_Identity()
        {
            var q = Quaternion.identity;
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            // Identity quaternion: (0, 0, 0, 1) or equivalent
            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f, "Identity quaternion should round-trip accurately");
        }

        [Test]
        public void CompressedRotation_90Degrees_Y()
        {
            var q = Quaternion.Euler(0, 90, 0);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f, "90-degree Y rotation should round-trip accurately");
        }

        [Test]
        public void CompressedRotation_45Degrees_All()
        {
            var q = Quaternion.Euler(45, 45, 45);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f, "45-degree all-axis rotation should round-trip accurately");
        }

        [Test]
        public void CompressedRotation_180Degrees()
        {
            var q = Quaternion.Euler(180, 0, 0);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.98f, "180-degree rotation should round-trip reasonably");
        }

        [Test]
        public void CompressedRotation_AxisAligned_X()
        {
            var q = Quaternion.Euler(90, 0, 0);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f);
        }

        [Test]
        public void CompressedRotation_AxisAligned_Z()
        {
            var q = Quaternion.Euler(0, 0, 90);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f);
        }

        [Test]
        public void CompressedRotation_Size_Is4Bytes()
        {
            var w = new NetWriter();
            w.WriteCompressedRotation(P2PSpringSync.CompressRotation(Quaternion.identity));
            Assert.AreEqual(4, w.Position);
        }

        [Test]
        public void CompressedRotation_MultipleValues_Consistent()
        {
            // Verify same input always produces same output
            var q = Quaternion.Euler(30, 60, 90);
            uint c1 = P2PSpringSync.CompressRotation(q);
            uint c2 = P2PSpringSync.CompressRotation(q);
            Assert.AreEqual(c1, c2);
        }

        [Test]
        public void CompressedRotation_ManyAngles()
        {
            // Test a range of angles to ensure no crashes or extreme inaccuracy
            for (int x = 0; x < 360; x += 30)
            {
                for (int y = 0; y < 360; y += 30)
                {
                    var q = Quaternion.Euler(x, y, 0);
                    uint compressed = P2PSpringSync.CompressRotation(q);
                    var result = P2PSpringSync.DecompressRotation(compressed);
                    float dot = Quaternion.Dot(q, result);
                    Assert.GreaterOrEqual(Mathf.Abs(dot), 0.95f,
                        $"Failed at ({x}, {y}, 0): dot={dot}");
                }
            }
        }

        [Test]
        public void CompressedRotation_NegativeW()
        {
            // Quaternions with negative w should still compress/decompress correctly
            // (smallest-three always picks the largest component and reconstructs it positive)
            var q = new Quaternion(0.5f, 0.5f, 0.5f, -0.5f);
            q = Quaternion.Normalize(q);
            uint compressed = P2PSpringSync.CompressRotation(q);
            var result = P2PSpringSync.DecompressRotation(compressed);

            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.98f);
        }

        [Test]
        public void CompressedRotation_WriteRead_RoundTrip()
        {
            var q = Quaternion.Euler(15, 77, 133);
            uint compressed = P2PSpringSync.CompressRotation(q);

            var w = new NetWriter();
            w.WriteUInt32(compressed);
            var r = new NetReader(w.ToArray());
            uint readBack = r.ReadUInt32();

            Assert.AreEqual(compressed, readBack);
            var result = P2PSpringSync.DecompressRotation(readBack);
            float dot = Quaternion.Dot(q, result);
            Assert.GreaterOrEqual(Mathf.Abs(dot), 0.99f);
        }

        #endregion
    }
}
