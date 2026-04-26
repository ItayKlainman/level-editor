using NUnit.Framework;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor.Tests
{
    public class StringIntMappingTests
    {
        private StringIntMapping CreateMapping(params (string key, int value)[] entries)
        {
            var mapping = ScriptableObject.CreateInstance<StringIntMapping>();
            foreach (var (key, value) in entries)
                mapping.Add(key, value);
            return mapping;
        }

        [Test]
        public void TryGet_KnownKey_ReturnsTrueAndCorrectValue()
        {
            var mapping = CreateMapping(("pink", 7), ("blue", 1));
            Assert.IsTrue(mapping.TryGet("pink", out int value));
            Assert.AreEqual(7, value);
        }

        [Test]
        public void TryGet_UnknownKey_ReturnsFalseAndZero()
        {
            var mapping = CreateMapping(("pink", 7));
            Assert.IsFalse(mapping.TryGet("unknown", out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void Get_KnownKey_ReturnsValue()
        {
            var mapping = CreateMapping(("red", 9));
            Assert.AreEqual(9, mapping.Get("red"));
        }

        [Test]
        public void Get_UnknownKey_ReturnsFallback()
        {
            var mapping = CreateMapping(("pink", 7));
            Assert.AreEqual(-1, mapping.Get("missing", fallback: -1));
        }

        [Test]
        public void Get_IsCaseSensitive()
        {
            var mapping = CreateMapping(("Pink", 7));
            Assert.AreEqual(0, mapping.Get("pink"));
        }

        [Test]
        public void Entries_ReturnsAllAddedEntries()
        {
            var mapping = CreateMapping(("a", 1), ("b", 2));
            Assert.AreEqual(2, mapping.Entries.Count);
        }
    }
}
