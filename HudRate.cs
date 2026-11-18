// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;

namespace UnknownsSmokescreen;

internal sealed class HudRate
{
    private const int MinFps = 5;
    private int rate, previousLimit, previousBudget;
    private float adjustAt;
    private double cost, frameSeconds;

    internal int Update(float now, float frameTime, int limit, int budgetTenths)
    {
        if (budgetTenths == 0 || limit != previousLimit || budgetTenths != previousBudget)
        {
            previousLimit = rate = limit;
            previousBudget = budgetTenths;
            cost = frameSeconds = 0;
            adjustAt = now + 1;
        }
        if (budgetTenths == 0) return rate;

        // Einzelne Fokus- oder Ladepausen sollen die Rate nicht dauerhaft bestimmen.
        if (frameTime > 0 && frameTime < 0.25f)
            frameSeconds = Smooth(frameSeconds, frameTime, 0.05);
        if (now < adjustAt || cost == 0 || frameSeconds == 0) return rate;
        adjustAt = now + 1;
        double affordable = (budgetTenths / 10.0) / (cost * frameSeconds);
        int target = (int)Math.Floor(Math.Clamp(affordable, MinFps, limit));
        if (affordable < rate * 0.95) rate = target;
        else if (affordable >= (rate + 1) * 1.15) rate = Math.Min(rate + 2, target);
        return rate;
    }

    internal void Sample(double milliseconds)
    {
        if (previousBudget != 0 && milliseconds > 0)
            cost = Smooth(cost, milliseconds, 0.15);
    }

    private static double Smooth(double old, double value, double weight) =>
        old == 0 ? value : old + (value - old) * weight;
}
