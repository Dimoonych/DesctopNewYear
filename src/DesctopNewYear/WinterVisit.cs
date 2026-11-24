namespace DesctopNewYear;

internal sealed class WinterVisit(YardSettings settings, Random random)
{
    private enum Visit { Waiting, Arriving, LeavingGift, Departing }
    private Visit phase;
    private float elapsed;
    private int tree;

    public bool InMotion => phase != Visit.Waiting;

    public ActorFrame? Frame(YardLayout layout)
    {
        if (!InMotion)
            return null;

        var entrance = new PointF(layout.View.Left - 78 * layout.Scale, layout.PondTree(0).Y - 65 * layout.Scale);
        var stand = GiftSpot(layout, tree);
        stand.X -= 35 * layout.Scale;
        var amount = phase switch
        {
            Visit.Arriving => Math.Clamp(elapsed / 5.5f, 0, 1),
            Visit.Departing => 1 - Math.Clamp(elapsed / 5f, 0, 1),
            _ => 1
        };
        var feet = new PointF(entrance.X + (stand.X - entrance.X) * amount,
            entrance.Y + (stand.Y - entrance.Y) * amount);
        var direction = phase == Visit.Departing ? -1 : 1;
        return new(feet, false, Pose.FatherFrost, direction, -1, phase == Visit.LeavingGift ? 0 : elapsed,
            SpecialGift: phase == Visit.Arriving);
    }

    public void Advance(float seconds, DateTime local, bool dark, bool snowing)
    {
        var day = DateOnly.FromDateTime(local).DayNumber;
        switch (phase)
        {
            case Visit.Waiting:
                if (settings.NextWinterVisit == 0)
                    settings.NextWinterVisit = day + random.Next(1, 4);
                if (!dark || !snowing || local.Hour < 18 || day < settings.NextWinterVisit ||
                    settings.WinterGiftTree >= 0)
                    return;
                var undecorated = Enumerable.Range(0, YardLayout.PondTreeCount)
                    .Where(index => (settings.WinterStars & (1 << index)) == 0).ToArray();
                tree = undecorated.Length == 0 ? random.Next(YardLayout.PondTreeCount) :
                    undecorated[random.Next(undecorated.Length)];
                SetPhase(Visit.Arriving);
                break;
            case Visit.Arriving when elapsed >= 5.5f:
                settings.WinterGiftTree = tree;
                settings.WinterGiftDay = day;
                settings.NextWinterVisit = day + random.Next(3, 6);
                SetPhase(Visit.LeavingGift);
                break;
            case Visit.LeavingGift when elapsed >= 1.2f:
                SetPhase(Visit.Departing);
                break;
            case Visit.Departing when elapsed >= 5:
                SetPhase(Visit.Waiting);
                break;
        }
        if (InMotion)
            elapsed += Math.Min(seconds, .085f);
    }

    public static PointF GiftSpot(YardLayout layout, int tree)
    {
        var position = layout.PondTree(tree);
        return new(position.X + 29 * layout.Scale, position.Y + 5 * layout.Scale);
    }

    private void SetPhase(Visit next)
    {
        phase = next;
        elapsed = 0;
    }
}
