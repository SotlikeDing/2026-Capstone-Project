using System.Numerics;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Domain;

public readonly record struct ExactRational : IComparable<ExactRational>
{
    public ExactRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("An exact rational denominator cannot be zero.");
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public static ExactRational Zero => new(BigInteger.Zero, BigInteger.One);

    public static ExactRational From(RowlandWardRational value) =>
        new(value.Numerator, value.Denominator);

    public static ExactRational operator +(ExactRational left, ExactRational right) =>
        new(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator
        );

    public static ExactRational operator *(ExactRational left, ExactRational right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static ExactRational operator /(ExactRational left, ExactRational right)
    {
        if (right.Numerator == 0)
        {
            throw new DivideByZeroException("Cannot divide by a zero exact rational.");
        }

        return new(
            left.Numerator * right.Denominator,
            left.Denominator * right.Numerator
        );
    }

    public int CompareTo(ExactRational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool IsWholeMultipleOf(ExactRational increment)
    {
        if (increment.Numerator <= 0)
        {
            return false;
        }

        var numerator = Numerator * increment.Denominator;
        var denominator = Denominator * increment.Numerator;
        return numerator % denominator == 0;
    }

    public ExactRational RoundHalfUpToWhole()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        if (remainder * 2 >= Denominator)
        {
            quotient += 1;
        }

        return new ExactRational(quotient, BigInteger.One);
    }

    public RowlandWardRational ToContractValue()
    {
        if (Numerator < long.MinValue || Numerator > long.MaxValue ||
            Denominator < 1 || Denominator > long.MaxValue)
        {
            throw new RowlandWardEvaluationException(
                "Die Rowland Ward-meting is te groot om veilig te verwerk."
            );
        }

        return new RowlandWardRational
        {
            Numerator = (long)Numerator,
            Denominator = (long)Denominator
        };
    }
}
