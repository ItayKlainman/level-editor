using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hoppa.AudioBalance;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The profile, its categories, its per-clip settings and the baked table are all editable
    /// in the default Inspector, not only through the Audio Balance window. A designer who
    /// opens the asset directly gets no window hints and no guide button, so a serialized field
    /// with no <see cref="TooltipAttribute"/> is genuinely undocumented at the point of use.
    ///
    /// <para>
    /// <b>This asserts that a tooltip EXISTS, never what it says.</b> Pinning wording would
    /// break on every copy edit while proving nothing, and this initiative has already shipped
    /// seven tests that passed against broken code. Presence is the invariant that actually has
    /// a failure mode worth catching: a field added later, or an attribute dropped in a
    /// refactor, silently ships an unexplained control. Deleting any one <c>[Tooltip]</c> in
    /// this package fails this test, which is the whole bar it needs to clear.
    /// </para>
    /// </summary>
    public class InspectorTooltipTests
    {
        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            return type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.GetCustomAttribute<NonSerializedAttribute>() == null)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null);
        }

        private static void AssertEveryFieldHasATooltip(Type type)
        {
            var fields = SerializedFields(type).ToArray();

            // Guards the guard: if the reflection filter ever stops matching -- a rename, a move
            // to properties -- an empty set would make the loop below pass vacuously, which is
            // exactly the shape of a test that proves nothing.
            Assert.IsNotEmpty(fields, $"{type.Name} exposed no serialized fields to check.");

            foreach (var field in fields)
            {
                var tooltip = field.GetCustomAttribute<TooltipAttribute>();

                Assert.IsNotNull(tooltip,
                    $"{type.Name}.{field.Name} is edited in the Inspector but has no [Tooltip].");

                Assert.IsNotEmpty(tooltip.tooltip ?? string.Empty,
                    $"{type.Name}.{field.Name} has an empty [Tooltip].");
            }
        }

        [Test]
        public void AudioBalanceProfile_SerializedFields_AllHaveTooltips()
        {
            AssertEveryFieldHasATooltip(typeof(AudioBalanceProfile));
        }

        [Test]
        public void AudioCategory_SerializedFields_AllHaveTooltips()
        {
            AssertEveryFieldHasATooltip(typeof(AudioCategory));
        }

        [Test]
        public void ClipSettings_SerializedFields_AllHaveTooltips()
        {
            AssertEveryFieldHasATooltip(typeof(ClipSettings));
        }

        [Test]
        public void AudioGainTable_SerializedFields_AllHaveTooltips()
        {
            AssertEveryFieldHasATooltip(typeof(AudioGainTable));
        }

        [Test]
        public void AudioGainTableEntry_SerializedFields_AllHaveTooltips()
        {
            AssertEveryFieldHasATooltip(typeof(AudioGainTable.Entry));
        }

        /// <summary>
        /// Tooltip text renders in Unity 2022.3 IMGUI, whose default font cannot be relied on
        /// for anything outside ASCII -- an emoji or a geometric glyph degrades to a blank box.
        /// This is a property of the text rather than of its wording, so it survives copy edits.
        /// </summary>
        [Test]
        public void EveryTooltip_IsAscii()
        {
            var types = new[]
            {
                typeof(AudioBalanceProfile), typeof(AudioCategory), typeof(ClipSettings),
                typeof(AudioGainTable), typeof(AudioGainTable.Entry)
            };

            foreach (var type in types)
            {
                foreach (var field in SerializedFields(type))
                {
                    var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                    if (tooltip == null)
                    {
                        continue;
                    }

                    var offending = tooltip.tooltip.FirstOrDefault(c => c > '~' || c < ' ');

                    Assert.AreEqual('\0', offending,
                        $"{type.Name}.{field.Name} tooltip contains non-ASCII '{offending}'.");
                }
            }
        }
    }
}
