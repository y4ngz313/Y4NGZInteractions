using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// #660: normalizing the rest shin must be followed by the body's natural animation pass
    /// before rendering, or the player sees a leg pop. AnimatePhysics waits for FixedUpdate;
    /// missing players and inactive animators are dropped without a write.
    /// </summary>
    public class LegBindPoseDrainPolicyTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NormalBodyNormalizesInEitherPhase(bool physicsStep)
        {
            Assert.Equal(LegBindPoseDrainAction.Normalize, LegBindPoseDrainPolicy.Decide(
                playerPresent: true, animatorActive: true, animatePhysics: false, physicsStep));
        }

        [Fact]
        public void AnimatePhysicsBodyStaysPendingInUpdate()
        {
            Assert.Equal(LegBindPoseDrainAction.KeepPending, LegBindPoseDrainPolicy.Decide(
                playerPresent: true, animatorActive: true, animatePhysics: true, physicsStep: false));
        }

        [Fact]
        public void AnimatePhysicsBodyNormalizesInFixedUpdate()
        {
            Assert.Equal(LegBindPoseDrainAction.Normalize, LegBindPoseDrainPolicy.Decide(
                playerPresent: true, animatorActive: true, animatePhysics: true, physicsStep: true));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void InactiveAnimatorDropsWithoutNormalization(bool animatePhysics, bool physicsStep)
        {
            Assert.Equal(LegBindPoseDrainAction.DropAnimatorInactive, LegBindPoseDrainPolicy.Decide(
                playerPresent: true, animatorActive: false, animatePhysics, physicsStep));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void MissingPlayerDropsBeforeAnimatorState(
            bool animatorActive, bool animatePhysics, bool physicsStep)
        {
            Assert.Equal(LegBindPoseDrainAction.DropPlayerMissing, LegBindPoseDrainPolicy.Decide(
                playerPresent: false, animatorActive, animatePhysics, physicsStep));
        }
    }
}
