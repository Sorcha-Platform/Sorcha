// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Persona;

/// <summary>
/// Seam over <see cref="System.Random"/> so persona tests can be deterministic.
/// </summary>
public interface IRandomSource
{
    int NextInt(int minInclusive, int maxInclusive);
    decimal NextDecimal(decimal minInclusive, decimal maxInclusive, int precision);
    T Choose<T>(IReadOnlyList<T> options);
}

/// <summary>
/// Production <see cref="IRandomSource"/> backed by <see cref="Random"/>.
/// </summary>
public sealed class RandomSource : IRandomSource
{
    private readonly Random _random;

    public RandomSource() : this(Random.Shared) { }

    public RandomSource(Random random)
    {
        _random = random;
    }

    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentException("maxInclusive must be >= minInclusive");
        return _random.Next(minInclusive, maxInclusive + 1);
    }

    public decimal NextDecimal(decimal minInclusive, decimal maxInclusive, int precision)
    {
        if (maxInclusive < minInclusive)
            throw new ArgumentException("maxInclusive must be >= minInclusive");
        if (precision < 0)
            throw new ArgumentException("precision must be >= 0");

        var range = maxInclusive - minInclusive;
        var sample = (decimal)_random.NextDouble();
        var value = minInclusive + (range * sample);
        return Math.Round(value, precision, MidpointRounding.AwayFromZero);
    }

    public T Choose<T>(IReadOnlyList<T> options)
    {
        if (options.Count == 0)
            throw new ArgumentException("options must be non-empty");
        return options[_random.Next(options.Count)];
    }
}
