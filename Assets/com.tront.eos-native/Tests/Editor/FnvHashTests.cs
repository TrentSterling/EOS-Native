using NUnit.Framework;
using EOSNative.Net;

namespace EOSNative.Tests
{
    public class FnvHashTests
    {
        [Test]
        public void EmptyString_ReturnsZero()
        {
            Assert.AreEqual(0u, NetworkManager.FnvHash(""));
        }

        [Test]
        public void NullString_ReturnsZero()
        {
            Assert.AreEqual(0u, NetworkManager.FnvHash(null));
        }

        [Test]
        public void Consistency_SameInput_SameOutput()
        {
            uint hash1 = NetworkManager.FnvHash("TakeDamage");
            uint hash2 = NetworkManager.FnvHash("TakeDamage");
            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void DifferentInputs_DifferentHashes()
        {
            uint h1 = NetworkManager.FnvHash("TakeDamage");
            uint h2 = NetworkManager.FnvHash("ApplyHeal");
            uint h3 = NetworkManager.FnvHash("Respawn");
            Assert.AreNotEqual(h1, h2);
            Assert.AreNotEqual(h2, h3);
            Assert.AreNotEqual(h1, h3);
        }

        [Test]
        public void KnownValue_FnvSpec()
        {
            // FNV-1a hash of "a" should be specific value
            uint hash = NetworkManager.FnvHash("a");
            Assert.AreNotEqual(0u, hash);
            // FNV-1a("a") = 0xe40c292c (per FNV spec)
            Assert.AreEqual(0xe40c292cu, hash);
        }

        [Test]
        public void SingleChar_NonZero()
        {
            uint hash = NetworkManager.FnvHash("x");
            Assert.AreNotEqual(0u, hash);
        }

        [Test]
        public void CaseSensitive()
        {
            uint lower = NetworkManager.FnvHash("abc");
            uint upper = NetworkManager.FnvHash("ABC");
            Assert.AreNotEqual(lower, upper);
        }

        [Test]
        public void CollisionResistance_CommonMethodNames()
        {
            var hashes = new System.Collections.Generic.HashSet<uint>();
            string[] names = {
                "TakeDamage", "ApplyHeal", "Respawn", "Fire", "Jump",
                "Move", "Attack", "Dodge", "Block", "Cast",
                "Interact", "PickUp", "Drop", "Use", "Equip",
                "ChatBubble", "PlayEffect", "ChangeColor", "ApplyImpulse"
            };
            foreach (var name in names)
            {
                uint hash = NetworkManager.FnvHash(name);
                Assert.IsTrue(hashes.Add(hash), $"Hash collision for '{name}'");
            }
        }
    }
}
