using NUnit.Framework;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor.Tests
{
    public class GameProfileExtensionsTests
    {
        private sealed class FakeExtensionA : ScriptableObject { }
        private sealed class FakeExtensionB : ScriptableObject { }

        [Test]
        public void GetExtension_ReturnsNull_WhenListEmpty()
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            Assert.IsNull(profile.GetExtension<FakeExtensionA>());
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void GetExtension_ReturnsMatchingType_WhenPresent()
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var a = ScriptableObject.CreateInstance<FakeExtensionA>();
            var b = ScriptableObject.CreateInstance<FakeExtensionB>();
            // Inject via reflection — keeps GameProfile.cs free of test-only setters.
            var field = typeof(GameProfile).GetField("_extensions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "GameProfile is missing the _extensions field.");
            field.SetValue(profile, new System.Collections.Generic.List<ScriptableObject> { a, b });

            Assert.AreSame(a, profile.GetExtension<FakeExtensionA>());
            Assert.AreSame(b, profile.GetExtension<FakeExtensionB>());

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(profile);
        }
    }
}
