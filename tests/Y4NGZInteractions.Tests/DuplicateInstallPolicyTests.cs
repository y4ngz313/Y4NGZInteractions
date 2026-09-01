using System;
using System.IO;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The duplicate-install detector must name every same-named DLL that is not the loaded file
    /// (a skipped stale copy silently starves every consumer with
    /// interaction_animation_api_not_initialized) while never flagging the loaded file itself
    /// under any spelling of its path.
    /// </summary>
    public class DuplicateInstallPolicyTests
    {
        private static readonly string Root = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "plugins"));

        private static string P(params string[] parts)
        {
            string combined = Root;
            foreach (string part in parts)
                combined = Path.Combine(combined, part);
            return combined;
        }

        [Fact]
        public void FlagsAForeignFileAndSkipsTheLoadedOne()
        {
            string loaded = P("Y4NGZInteractions", "Y4NGZInteractions.dll");
            string stale = P("Y4NGZ313-Y4NGZInteractions", "Y4NGZInteractions.dll");

            string[] shadows = DuplicateInstallPolicy.FindShadowCopies(
                loaded, new[] { stale, loaded });

            Assert.Equal(new[] { stale }, shadows);
        }

        [Fact]
        public void MatchesTheLoadedFileCaseInsensitivelyAndAfterNormalization()
        {
            string loaded = P("Y4NGZInteractions", "Y4NGZInteractions.dll");
            string sameSpelledDifferently = Path.Combine(
                Root, "Y4NGZInteractions", ".", "Y4NGZINTERACTIONS.DLL");

            string[] shadows = DuplicateInstallPolicy.FindShadowCopies(
                loaded, new[] { sameSpelledDifferently, "  " + loaded + "  " });

            Assert.Empty(shadows);
        }

        [Fact]
        public void DeduplicatesAndSortsShadowPaths()
        {
            string loaded = P("Y4NGZInteractions", "Y4NGZInteractions.dll");
            string staleA = P("A-old", "Y4NGZInteractions.dll");
            string staleB = P("B-old", "Y4NGZInteractions.dll");

            string[] shadows = DuplicateInstallPolicy.FindShadowCopies(
                loaded, new[] { staleB, staleA, staleB.ToUpperInvariant() });

            Assert.Equal(2, shadows.Length);
            Assert.Equal(staleA, shadows[0], ignoreCase: true);
            Assert.Equal(staleB, shadows[1], ignoreCase: true);
        }

        [Fact]
        public void UnknownLoadedLocationReportsNothing()
        {
            string stale = P("Y4NGZ313-Y4NGZInteractions", "Y4NGZInteractions.dll");

            Assert.Empty(DuplicateInstallPolicy.FindShadowCopies(null, new[] { stale }));
            Assert.Empty(DuplicateInstallPolicy.FindShadowCopies(string.Empty, new[] { stale }));
            Assert.Empty(DuplicateInstallPolicy.FindShadowCopies("   ", new[] { stale }));
        }

        [Fact]
        public void IgnoresNullEmptyAndUnusableCandidates()
        {
            string loaded = P("Y4NGZInteractions", "Y4NGZInteractions.dll");

            string[] shadows = DuplicateInstallPolicy.FindShadowCopies(
                loaded, new[] { null, string.Empty, "   ", "\0invalid\0" });

            Assert.Empty(shadows);
        }

        [Fact]
        public void NullCandidateListReportsNothing()
        {
            string loaded = P("Y4NGZInteractions", "Y4NGZInteractions.dll");

            Assert.Empty(DuplicateInstallPolicy.FindShadowCopies(loaded, null));
        }
    }
}
