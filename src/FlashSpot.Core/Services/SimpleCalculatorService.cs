using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;

namespace FlashSpot.Core.Services;

public sealed class SimpleCalculatorService : ICalculatorService
{
    private static readonly Regex AllowedExpression =
        new(@"^[0-9\.\+\-\*\/\%\(\)\s]+$", RegexOptions.Compiled);

    public bool TryEvaluate(string query, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.Trim();
        if (!AllowedExpression.IsMatch(trimmed))
        {
            return false;
        }

        try
        {
            using var table = new DataTable();
            var raw = table.Compute(trimmed, string.Empty);
            if (raw is null)
            {
                return false;
            }

            value = Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
                .ToString("0.############", CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

