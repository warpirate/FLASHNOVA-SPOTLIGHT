using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;
using NCalc;

namespace FlashSpot.Core.Services;

public sealed partial class NCalcCalculatorService : ICalculatorService
{
    public bool TryEvaluate(string query, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        // Must contain at least one digit or math-related keyword
        if (!ContainsMathContent().IsMatch(query))
        {
            return false;
        }

        try
        {
            var expression = new Expression(query, ExpressionOptions.DecimalAsDefault);

            expression.EvaluateParameter += (name, args) =>
            {
                switch (name.ToLowerInvariant())
                {
                    case "pi":
                        args.Result = (decimal)Math.PI;
                        break;
                    case "e":
                        args.Result = (decimal)Math.E;
                        break;
                }
            };

            var result = expression.Evaluate();

            if (result is null)
            {
                return false;
            }

            value = result switch
            {
                decimal d => d.ToString("G"),
                double d => d.ToString("G"),
                float f => f.ToString("G"),
                int i => i.ToString(),
                long l => l.ToString(),
                bool b => b.ToString(),
                _ => result.ToString() ?? string.Empty
            };

            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"[\d]", RegexOptions.Compiled)]
    private static partial Regex ContainsMathContent();
}
