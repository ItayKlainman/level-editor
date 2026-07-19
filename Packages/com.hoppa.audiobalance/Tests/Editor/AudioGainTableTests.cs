using System.Collections.Generic;
using Hoppa.AudioBalance;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class AudioGainTableTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        [Test]
        public void LinearFromDb_IsCorrectAtKnownPoints()
        {
            Assert.AreEqual(1.0f, AudioGainMath.LinearFromDb(0f), 1e-4f);
            Assert.AreEqual(0.5012f, AudioGainMath.LinearFromDb(-6f), 1e-3f);
            Assert.AreEqual(0.1f, AudioGainMath.LinearFromDb(-20f), 1e-4f);
        }

        [Test]
        public void DbFromLinear_RoundTripsWithLinearFromDb()
        {
            Assert.AreEqual(-13.5f, AudioGainMath.DbFromLinear(AudioGainMath.LinearFromDb(-13.5f)), 1e-3f);
        }

        [Test]
        public void DbFromLinear_ClampsAtZeroInsteadOfReturningNegativeInfinity()
        {
            Assert.AreEqual(AudioGainMath.MinDb, AudioGainMath.DbFromLinear(0f), 1e-4f);
        }

        [Test]
        public void GetGainDb_ReturnsStoredValue()
        {
            var clip = MakeClip("stored");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -7.5f }
            });

            Assert.AreEqual(-7.5f, table.GetGainDb(clip), 1e-4f);
        }

        [Test]
        public void GetGain_ReturnsUnityGainForUnknownClip()
        {
            var known = MakeClip("known");
            var unknown = MakeClip("unknown");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = known, GainDb = -12f }
            });

            Assert.AreEqual(1f, table.GetGain(unknown), 1e-4f,
                "An unknown clip must never be silenced by a missing table entry.");
        }

        [Test]
        public void GetGainDb_HandlesNullClipWithoutThrowing()
        {
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            Assert.AreEqual(0f, table.GetGainDb(null), 1e-4f);
        }

        [Test]
        public void SetEntries_ReplacesPreviousLookup()
        {
            var clip = MakeClip("replaced");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -3f }
            });
            Assert.AreEqual(-3f, table.GetGainDb(clip), 1e-4f);

            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -9f }
            });
            Assert.AreEqual(-9f, table.GetGainDb(clip), 1e-4f,
                "The cached lookup must be invalidated when entries are replaced.");
        }
    }
}
