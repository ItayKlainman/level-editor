using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class GainTableWriterTests
    {
        private static AudioBalanceRow Row(string name, float gainDb, ClipStatus status = ClipStatus.Ok)
        {
            var clip = AudioClip.Create(name, 128, 1, 44100, false);
            return new AudioBalanceRow
            {
                Clip = clip,
                Analysis = status == ClipStatus.Ok
                    ? ClipAnalysis.Ok(clip, -20f, -3f)
                    : new ClipAnalysis(clip, status, 0f, -80f, "reason"),
                Gain = new GainResult(clip, status, gainDb, gainDb, false)
            };
        }

        [Test]
        public void BuildEntries_IncludesAnalyzableRowsWithTheirFinalGain()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -6f), Row("snare", 0f) };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(-6f, entries.First(e => e.Clip.name == "kick").GainDb, 1e-4f);
            Assert.AreEqual(0f, entries.First(e => e.Clip.name == "snare").GainDb, 1e-4f);
        }

        [Test]
        public void BuildEntries_SkipsSilentAndUnanalyzableRows()
        {
            var rows = new List<AudioBalanceRow>
            {
                Row("good", -3f),
                Row("mute", 0f, ClipStatus.Silent),
                Row("broken", 0f, ClipStatus.Unanalyzable)
            };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("good", entries[0].Clip.name);
        }

        /// <summary>
        /// A row can carry an Ok ANALYSIS and still never have been solved -- the two are
        /// separate fields, filled by separate passes. The unsolved value is
        /// <c>default(GainResult)</c>, whose <c>FinalGainDb</c> is <b>0 dB</b>: a completely
        /// plausible-looking number that silently means "leave this clip at full volume". Baking
        /// that is worse than omitting the row, because the runtime lookup falls through to unity
        /// gain for a missing entry anyway -- so omitting costs nothing and asserting nothing
        /// costs a wrong bake.
        ///
        /// <para>
        /// The discriminator is <c>Gain.Clip != null</c>, never <c>Gain.Status</c>:
        /// <c>ClipStatus.Ok</c> is <c>0</c>, so an unsolved <c>default(GainResult)</c> reports
        /// <c>Ok</c> and a status check here would be vacuous by construction.
        /// </para>
        /// </summary>
        [Test]
        public void BuildEntries_SkipsARowWhoseGainWasNeverSolved()
        {
            var unsolved = Row("never-solved", 0f);
            unsolved.Gain = default;

            var rows = new List<AudioBalanceRow> { Row("solved", -3f), unsolved };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual(1, entries.Count,
                "An unsolved row bakes a 0 dB entry that looks deliberate but is not.");
            Assert.AreEqual("solved", entries[0].Clip.name);
        }

        [Test]
        public void BuildEntries_IsDeterministicallyOrderedByClipName()
        {
            var rows = new List<AudioBalanceRow> { Row("zebra", -1f), Row("apple", -2f) };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual("apple", entries[0].Clip.name,
                "A stable order keeps the asset's git diff clean between runs.");
            Assert.AreEqual("zebra", entries[1].Clip.name);
        }

        [Test]
        public void BuildEntries_OnNullOrEmptyInput_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, GainTableWriter.BuildEntries(null).Count);
            Assert.AreEqual(0, GainTableWriter.BuildEntries(new List<AudioBalanceRow>()).Count);
        }

        [Test]
        public void EntriesFedIntoATable_AreReadableThroughTheRuntimeLookup()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -6f) };
            var table = ScriptableObject.CreateInstance<AudioGainTable>();

            table.SetEntries(GainTableWriter.BuildEntries(rows));

            Assert.AreEqual(-6f, table.GetGainDb(rows[0].Clip), 1e-4f);
            Assert.AreEqual(0.5012f, table.GetGain(rows[0].Clip), 1e-3f);
        }

        [Test]
        public void Write_WithNoTableAssigned_ReturnsFalseWithoutThrowing()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();

            Assert.IsFalse(GainTableWriter.Write(profile, new List<AudioBalanceRow> { Row("kick", -6f) }));
        }

        [Test]
        public void Write_WithNullProfile_ReturnsFalseWithoutThrowing()
        {
            Assert.IsFalse(GainTableWriter.Write(null, new List<AudioBalanceRow>()));
        }

        [Test]
        public void Write_PopulatesTheAssignedTable()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.Table = ScriptableObject.CreateInstance<AudioGainTable>();

            var rows = new List<AudioBalanceRow> { Row("kick", -6f) };

            Assert.IsTrue(GainTableWriter.Write(profile, rows));
            Assert.AreEqual(-6f, profile.Table.GetGainDb(rows[0].Clip), 1e-4f);
        }
    }
}
