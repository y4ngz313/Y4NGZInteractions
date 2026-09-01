using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The bind-pose normalization must restore a target only when a pristine baseline exists
    /// AND the target actually deviates: restoring without a baseline would plant residue, and
    /// restoring within tolerance would churn transforms every equip for nothing. A crouched
    /// equip deviates ~0.96 m on the arms metarig Y — the defining case that must restore.
    /// </summary>
    public class ControllerSwapBindPosePolicyTests
    {
        private const float Tolerance = 0.001f;

        [Fact]
        public void CrouchedEquipDeviationRestores()
        {
            Assert.True(ControllerSwapBindPosePolicy.ShouldRestoreBeforeSwap(
                pristineAvailable: true, 0f, -0.963f, 0f, Tolerance));
        }

        [Theory]
        [InlineData(0.002f, 0f, 0f)]
        [InlineData(0f, 0.002f, 0f)]
        [InlineData(0f, 0f, 0.002f)]
        [InlineData(0f, -0.002f, 0f)]
        public void AnySingleAxisBeyondToleranceRestores(float dx, float dy, float dz)
        {
            Assert.True(ControllerSwapBindPosePolicy.ShouldRestoreBeforeSwap(
                pristineAvailable: true, dx, dy, dz, Tolerance));
        }

        [Fact]
        public void WithinToleranceDoesNotRestore()
        {
            Assert.False(ControllerSwapBindPosePolicy.ShouldRestoreBeforeSwap(
                pristineAvailable: true, 0.0005f, -0.0005f, 0.0005f, Tolerance));
        }

        [Fact]
        public void MissingPristineBaselineNeverRestores()
        {
            Assert.False(ControllerSwapBindPosePolicy.ShouldRestoreBeforeSwap(
                pristineAvailable: false, 0f, -0.963f, 0f, Tolerance));
        }
    }
}
