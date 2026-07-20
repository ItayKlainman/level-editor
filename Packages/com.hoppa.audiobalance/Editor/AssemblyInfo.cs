using System.Runtime.CompilerServices;

// The package's mutating lookups (AudioBalanceProfile.SettingsFor, AudioCategory's name
// setter) are internal so that neither a consumer nor a future render path can write to a
// profile asset just by reading from it. The test assembly still needs to build fixtures --
// enrolling clips is exactly what a fixture does -- so it is granted access here rather than
// the API being widened back to public for test convenience.
[assembly: InternalsVisibleTo("Hoppa.AudioBalance.Editor.Tests")]
