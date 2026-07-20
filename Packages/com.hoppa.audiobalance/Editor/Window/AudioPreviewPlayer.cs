using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Auditions clips with their solved gain applied.
    ///
    /// <para>
    /// Playback goes through the internal <c>UnityEditor.AudioUtil</c> because
    /// <see cref="AudioSource.Play"/> produces no audio outside Play Mode -- a scene
    /// AudioSource simply cannot preview anything from an EditorWindow. Gain is applied by
    /// pre-scaling the samples into a temporary clip rather than by a volume parameter,
    /// because AudioUtil exposes no volume control; hearing the gain is the entire point, and
    /// Unity's built-in clip preview was rejected for exactly that reason.
    /// </para>
    ///
    /// <para>
    /// <b>Untested boundary.</b> The reflection into <c>AudioUtil</c> has no automated coverage
    /// and cannot meaningfully get any: the type is internal, its method signatures have
    /// changed across Unity versions, and asserting that audio was <i>audibly</i> produced is
    /// not something an EditMode test can do. This is the same accepted-gap situation as
    /// <see cref="ClipSampleReader.StreamingError"/>, and it is handled the same way -- named
    /// here rather than left to be discovered. What <i>is</i> tested is
    /// <see cref="PreviewClipFactory"/>, which owns all the sample arithmetic. The reflection
    /// layer degrades to a single actionable warning if the method cannot be found, so a Unity
    /// upgrade that moves it produces a clear diagnostic rather than silence. Verifying that
    /// audio actually comes out is therefore a hands-on check, listed in the handover.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class AudioPreviewPlayer
    {
        private static AudioClip _temp;
        private static MethodInfo _play;
        private static MethodInfo _stop;
        private static bool _resolved;
        private static bool _warned;

        static AudioPreviewPlayer()
        {
            // A temp clip is HideAndDontSave, so nothing else will clean it up. Both of these
            // are points where the static field is about to be reset or the audio engine
            // restarted, leaving the clip orphaned but alive.
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.playModeStateChanged += _ => Teardown();
        }

        public static void PlayWithGain(AudioClip clip, float gainDb)
        {
            if (clip == null || !TryReadSamples(clip, out var samples))
            {
                return;
            }

            Play(BuildTemp(clip, PreviewClipFactory.Scale(samples, gainDb),
                clip.channels, clip.frequency));
        }

        /// <summary>Plays the clip mixed over the anchor bed, so it is judged in context.</summary>
        public static void PlayAgainstAnchor(AudioClip clip, float gainDb,
            AudioClip anchor, float anchorGainDb)
        {
            if (clip == null || !TryReadSamples(clip, out var clipSamples))
            {
                return;
            }

            if (anchor == null || !TryReadSamples(anchor, out var anchorSamples) ||
                anchor.channels != clip.channels || anchor.frequency != clip.frequency)
            {
                // Mixing mismatched channel counts or sample rates by index would pitch- and
                // pan-shift the result, which is worse than not offering the comparison.
                PlayWithGain(clip, gainDb);
                return;
            }

            Play(BuildTemp(clip,
                PreviewClipFactory.Mix(clipSamples, gainDb, anchorSamples, anchorGainDb),
                clip.channels, clip.frequency));
        }

        public static void StopAll()
        {
            if (!Resolve() || _stop == null)
            {
                return;
            }

            try
            {
                _stop.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioBalance] Could not stop preview playback: {e.Message}");
            }
        }

        /// <summary>Stops playback and destroys the temporary clip, so previews never leak one per click.</summary>
        public static void Teardown()
        {
            StopAll();
            DestroyTemp();
        }

        private static void DestroyTemp()
        {
            if (_temp != null)
            {
                UnityEngine.Object.DestroyImmediate(_temp);
            }

            _temp = null;
        }

        private static bool TryReadSamples(AudioClip clip, out float[] samples)
        {
            // Reuses the reader that already produces the actionable Streaming diagnostic,
            // so preview and analysis fail the same way for the same reason.
            if (ClipSampleReader.TryRead(clip, out samples, out var error))
            {
                return true;
            }

            Debug.LogWarning($"[AudioBalance] Cannot preview '{clip.name}': {error}");
            samples = null;
            return false;
        }

        /// <summary>
        /// The temp clip is a single static slot: the previous one is stopped and destroyed
        /// before a new one is built, and again on domain reload, play-mode change, and window
        /// close. One live temporary at a time, never one per click.
        /// </summary>
        private static AudioClip BuildTemp(AudioClip source, float[] samples, int channels, int frequency)
        {
            // Stop before destroy: destroying a clip AudioUtil is still playing is the one
            // ordering that can take the audio engine down with it.
            StopAll();
            DestroyTemp();

            if (samples == null || samples.Length == 0 || channels <= 0)
            {
                return null;
            }

            _temp = AudioClip.Create($"~preview_{source.name}",
                samples.Length / channels, channels, frequency, false);
            _temp.hideFlags = HideFlags.HideAndDontSave;
            _temp.SetData(samples, 0);

            return _temp;
        }

        private static void Play(AudioClip clip)
        {
            if (clip == null || !Resolve())
            {
                return;
            }

            try
            {
                // Signatures differ across versions: 2020+ is (AudioClip, int, bool), some
                // builds expose (AudioClip). Fill whatever the resolved overload wants rather
                // than binding a fixed argument list.
                var parameters = _play.GetParameters();
                var args = new object[parameters.Length];
                args[0] = clip;

                for (var i = 1; i < parameters.Length; i++)
                {
                    args[i] = parameters[i].ParameterType == typeof(bool)
                        ? (object)false
                        : Activator.CreateInstance(parameters[i].ParameterType);
                }

                _play.Invoke(null, args);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioBalance] Preview playback failed: {e.Message}");
            }
        }

        /// <summary>
        /// Resolves AudioUtil once. Returns false -- with ONE warning, not one per click --
        /// when the method cannot be found, so a Unity version that renames it degrades to an
        /// explicit diagnostic instead of a dead button.
        /// </summary>
        private static bool Resolve()
        {
            if (_resolved)
            {
                return _play != null;
            }

            _resolved = true;

            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (type != null)
            {
                const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic;

                // PlayPreviewClip is current; PlayClip is the pre-2020 name.
                _play = type.GetMethods(Flags).FirstOrDefault(m =>
                    (m.Name == "PlayPreviewClip" || m.Name == "PlayClip") &&
                    m.GetParameters().Length > 0 &&
                    m.GetParameters()[0].ParameterType == typeof(AudioClip));

                _stop = type.GetMethods(Flags).FirstOrDefault(m =>
                    (m.Name == "StopAllPreviewClips" || m.Name == "StopAllClips") &&
                    m.GetParameters().Length == 0);
            }

            if (_play == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning(
                    "[AudioBalance] Could not find UnityEditor.AudioUtil.PlayPreviewClip on " +
                    $"Unity {Application.unityVersion}. Clip preview is disabled; every other " +
                    "part of the window (analysis, gains, Write Table) is unaffected. " +
                    "This is an internal Unity API that moves between versions -- the fix is to " +
                    "widen the name list in Resolve(), not to silence this warning.");
            }

            return _play != null;
        }
    }
}
