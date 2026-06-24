using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakImageLibraryTests
    {
        [Test]
        public void ParseIdeas_TrimsSkipsCommentsAndDedupes()
        {
            string raw = "monkey eating banana\n  dolphin  \n\n# a comment\npopsicle\nDolphin\n";
            var ideas = YAKImageLibraryCore.ParseIdeas(raw);

            Assert.AreEqual(new List<string> { "monkey eating banana", "dolphin", "popsicle" }, ideas,
                "blank lines and # comments dropped; case-insensitive de-dupe keeps first 'dolphin'");
        }
    }
}
