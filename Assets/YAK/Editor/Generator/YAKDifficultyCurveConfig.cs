using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    [Serializable]
    public class TierPreset
    {
        public string Name = "Tier";
        [Header("Board")]
        public int GridWidth = 12;
        public int GridHeight = 12;
        [Min(2)] public int MaxColors = 3;
        [Header("Spools")]
        [Min(1)] public int AvgCapacity = 20;
        [Range(0f, 1f)] public float CapacitySlack = 0f;
        [Min(1)] public int ConveyorSlots = 5;
        public Vector2Int ColumnRange = new Vector2Int(2, 5);
        [Range(0f, 1f)] public float HiddenRatio = 0f;
        [Header("Difficulty target")]
        public float TargetAps = 3f;
        public float ApsTolerance = 0.6f;
        [Range(1, 10)] public int Complexity = 3;

        public TierPreset Clone() => (TierPreset)MemberwiseClone();
    }

    [Serializable]
    public class CurveSegment
    {
        public string TierName = "";
        [Min(0)] public int LevelCount = 1;
    }

    [CreateAssetMenu(menuName = "Hoppa/YAK/Generator/YAK Difficulty Curve")]
    public sealed class YAKDifficultyCurveConfig : ScriptableObject
    {
        public List<TierPreset> Presets = new List<TierPreset>();
        public List<CurveSegment> Curve = new List<CurveSegment>();

        public int TotalLevels()
        {
            int t = 0;
            if (Curve != null) foreach (var s in Curve) t += Mathf.Max(0, s.LevelCount);
            return t;
        }

        public TierPreset FindPreset(string name)
            => Presets?.FirstOrDefault(p => p.Name == name);

        public TierPreset TierForLevel(int oneBasedIndex)
        {
            if (Curve == null || Curve.Count == 0 || Presets == null || Presets.Count == 0)
                return null;
            if (oneBasedIndex < 1)
                return FindPreset(Curve[0].TierName) ?? Presets[0];

            int acc = 0;
            foreach (var seg in Curve)
            {
                acc += Mathf.Max(0, seg.LevelCount);
                if (oneBasedIndex <= acc)
                    return FindPreset(seg.TierName);
            }
            // beyond total → last segment's tier
            return FindPreset(Curve[Curve.Count - 1].TierName);
        }

        public TierPreset Duplicate(int index)
        {
            if (Presets == null || index < 0 || index >= Presets.Count) return null;
            var copy = Presets[index].Clone();
            copy.Name = Presets[index].Name + " Copy";
            Presets.Add(copy);
            return copy;
        }

        public void DeletePreset(int index)
        {
            if (Presets != null && index >= 0 && index < Presets.Count)
                Presets.RemoveAt(index);
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Presets == null || Presets.Count == 0) errors.Add("No tier presets defined.");
            if (Curve == null || Curve.Count == 0) errors.Add("Curve is empty — add at least one segment.");
            if (Curve != null && Presets != null)
            {
                foreach (var seg in Curve)
                    if (FindPreset(seg.TierName) == null)
                        errors.Add($"Curve segment references unknown tier '{seg.TierName}'.");
            }
            return errors;
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(TotalLevels()).Append(" levels");
            if (Curve != null && Curve.Count > 0)
            {
                sb.Append(" → ");
                sb.Append(string.Join(", ", Curve.Select(s => $"{s.TierName} ×{Mathf.Max(0, s.LevelCount)}")));
            }
            return sb.ToString();
        }
    }
}
