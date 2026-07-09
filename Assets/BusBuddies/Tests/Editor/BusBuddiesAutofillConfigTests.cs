using System.Reflection;
using NUnit.Framework;
using Hoppa.BusBuddies.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 2: new mapping constants + fresh-level defaults, and OnValidate clamps.
    public sealed class BusBuddiesAutofillConfigTests
    {
        private static void Validate(BusBuddiesAutofillConfig cfg)
            => cfg.GetType().GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                  .Invoke(cfg, null);

        [Test]
        public void Defaults_MatchExcelMappingAndFreshLevel()
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            Assert.AreEqual(10, cfg.ChunksBase);
            Assert.AreEqual(5, cfg.ChunksStep);
            Assert.AreEqual(0.10f, cfg.MainColorShareThreshold, 1e-5f);
            Assert.IsTrue(cfg.ExcludeOutlineFromMain);
            Assert.AreEqual(3, cfg.DefaultChunks);
            Assert.AreEqual(0.5f, cfg.DefaultDeviation, 1e-5f);
            Assert.AreEqual(3, cfg.DefaultColumns);
            Assert.AreEqual(3, cfg.DefaultDifficulty);
            Assert.IsFalse(cfg.DefaultNoSingleBusColor);
            Assert.IsFalse(cfg.DefaultRoundToFive);
        }

        [Test]
        public void OnValidate_ClampsNewKnobs()
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            cfg.ChunksBase = 0; cfg.ChunksStep = -3; cfg.MainColorShareThreshold = 5f;
            cfg.DefaultChunks = 99; cfg.DefaultColumns = 0; cfg.DefaultDifficulty = -1;
            cfg.DefaultDeviation = 3f;
            Validate(cfg);

            Assert.AreEqual(1, cfg.ChunksBase);
            Assert.AreEqual(0, cfg.ChunksStep);
            Assert.AreEqual(1f, cfg.MainColorShareThreshold, 1e-5f);
            Assert.AreEqual(5, cfg.DefaultChunks);
            Assert.AreEqual(1, cfg.DefaultColumns);
            Assert.AreEqual(1, cfg.DefaultDifficulty);
            Assert.AreEqual(1f, cfg.DefaultDeviation, 1e-5f);
        }
    }
}
