using DarkNights.Client;

namespace DarkNights.Tests;

/// <summary>
/// The no-fighting rule. These run the cases the game actually produces: a field rewritten
/// every frame, a field rewritten on a timer, and a field another mod sets once.
/// </summary>
public class TrackedTests
{
    [Fact]
    public void AFieldNobodyRewritesIsScaledOnce_NotCompounded()
    {
        var t = new Tracked();
        float field = 2f;

        for (int frame = 0; frame < 100; frame++)
        {
            field = t.Apply(field, 0.5f);
        }

        Assert.Equal(1f, field);
        Assert.Equal(2f, t.Base);
        Assert.Equal(0, t.BaseChanges);
    }

    [Fact]
    public void AFieldTheGameRewritesEveryFrameIsScaledFromEachNewValue()
    {
        var t = new Tracked();
        for (int frame = 0; frame < 10; frame++)
        {
            float gameValue = 1f + frame;
            Assert.Equal(gameValue * 0.5f, t.Apply(gameValue, 0.5f));
        }
    }

    [Fact]
    public void AFieldRewrittenOnATimer_LikeTodSkysLight_FollowsTheGameWithoutCompounding()
    {
        var t = new Tracked();
        float field = 0.15f;

        for (int frame = 0; frame < 60; frame++)
        {
            if (frame % 20 == 0)
            {
                field = 0.15f + frame * 0.001f;    // TOD_Sky's UpdateInterval tick
            }

            field = t.Apply(field, 0.6f);
            Assert.Equal(t.Base * 0.6f, field, 6);
        }

        Assert.Equal(0.15f + 40 * 0.001f, t.Base, 6);
    }

    [Fact]
    public void AnotherModsValueIsAdopted_AndTheNightAppliedOnTop()
    {
        var t = new Tracked();
        float field = t.Apply(6f, 0.5f);           // ours: 3
        field = 4f;                                // another mod sets the field
        field = t.Apply(field, 0.5f);

        Assert.Equal(4f, t.Base);
        Assert.Equal(2f, field);
        Assert.Equal(1, t.BaseChanges);
    }

    [Fact]
    public void AMultiplierOfOnePutsTheBaseBack()
    {
        var t = new Tracked();
        float field = t.Apply(5f, 0.2f);
        field = t.Apply(field, 1f);
        Assert.Equal(5f, field);
    }

    [Fact]
    public void RestoreReturnsTheBase_UnlessSomethingElseWroteSince()
    {
        var t = new Tracked();
        float field = t.Apply(5f, 0.2f);
        Assert.Equal(5f, t.Restore(field));

        var u = new Tracked();
        u.Apply(5f, 0.2f);
        Assert.Equal(7f, u.Restore(7f));
    }

    [Fact]
    public void TheCeilingFormIsTrackedTheSameWay()
    {
        var t = new Tracked();
        float upper = 6f;
        for (int frame = 0; frame < 50; frame++)
        {
            upper = t.Write(NightModel.ExposureCeiling(t.Adopt(upper), 1.5f, 1f));
        }

        Assert.Equal(1.5f, upper, 5);
        Assert.Equal(6f, t.Base);
    }
}
