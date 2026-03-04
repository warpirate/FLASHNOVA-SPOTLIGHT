using System.Text.RegularExpressions;

namespace FlashSpot.Core.Services;

public sealed partial class UnitConversionService
{
    private static readonly Dictionary<string, UnitFamily> _families = BuildFamilies();

    public bool TryConvert(string query, out string result)
    {
        result = string.Empty;

        var match = ConversionPattern().Match(query.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!decimal.TryParse(match.Groups["value"].Value, out var inputValue))
        {
            return false;
        }

        var fromUnit = NormalizeUnit(match.Groups["from"].Value);
        var toUnit = NormalizeUnit(match.Groups["to"].Value);

        // Find the family that contains both units
        foreach (var family in _families.Values)
        {
            if (family.Units.TryGetValue(fromUnit, out var fromDef)
                && family.Units.TryGetValue(toUnit, out var toDef))
            {
                decimal converted;
                if (family.Name == "Temperature")
                {
                    converted = ConvertTemperature(inputValue, fromUnit, toUnit);
                }
                else
                {
                    var baseValue = inputValue * fromDef.ToBase;
                    converted = baseValue / toDef.ToBase;
                }

                var fromDisplay = fromDef.Display;
                var toDisplay = toDef.Display;
                result = $"{inputValue} {fromDisplay} = {converted:G10} {toDisplay}";
                return true;
            }
        }

        return false;
    }

    private static decimal ConvertTemperature(decimal value, string from, string to)
    {
        // Convert to Celsius first
        var celsius = from switch
        {
            "c" or "celsius" => value,
            "f" or "fahrenheit" => (value - 32) * 5 / 9,
            "k" or "kelvin" => value - 273.15m,
            _ => value
        };

        // Convert from Celsius to target
        return to switch
        {
            "c" or "celsius" => celsius,
            "f" or "fahrenheit" => celsius * 9 / 5 + 32,
            "k" or "kelvin" => celsius + 273.15m,
            _ => celsius
        };
    }

    private static string NormalizeUnit(string unit)
    {
        var u = unit.Trim().ToLowerInvariant();

        // Remove trailing 's' for plurals (but not "celsius", "inches", etc.)
        if (u.EndsWith('s') && !u.EndsWith("us") && !u.EndsWith("es") && u.Length > 3)
        {
            u = u[..^1];
        }

        return _aliases.TryGetValue(u, out var normalized) ? normalized : u;
    }

    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Length
        ["mm"] = "millimeter", ["millimeters"] = "millimeter",
        ["cm"] = "centimeter", ["centimeters"] = "centimeter",
        ["m"] = "meter", ["meters"] = "meter",
        ["km"] = "kilometer", ["kilometers"] = "kilometer", ["kms"] = "kilometer",
        ["in"] = "inch", ["inches"] = "inch", ["\""] = "inch",
        ["ft"] = "foot", ["feet"] = "foot",
        ["yd"] = "yard", ["yards"] = "yard",
        ["mi"] = "mile", ["miles"] = "mile",

        // Weight
        ["mg"] = "milligram", ["milligrams"] = "milligram",
        ["g"] = "gram", ["grams"] = "gram",
        ["kg"] = "kilogram", ["kilograms"] = "kilogram", ["kgs"] = "kilogram",
        ["oz"] = "ounce", ["ounces"] = "ounce",
        ["lb"] = "pound", ["lbs"] = "pound", ["pounds"] = "pound",
        ["st"] = "stone", ["stones"] = "stone",
        ["t"] = "tonne", ["tonnes"] = "tonne", ["metric ton"] = "tonne",

        // Temperature
        ["c"] = "celsius", ["degc"] = "celsius",
        ["f"] = "fahrenheit", ["degf"] = "fahrenheit",
        ["k"] = "kelvin",

        // Volume
        ["ml"] = "milliliter", ["milliliters"] = "milliliter",
        ["l"] = "liter", ["liters"] = "liter", ["litres"] = "liter",
        ["gal"] = "gallon", ["gallons"] = "gallon",
        ["qt"] = "quart", ["quarts"] = "quart",
        ["pt"] = "pint", ["pints"] = "pint",
        ["cup"] = "cup", ["cups"] = "cup",
        ["tbsp"] = "tablespoon", ["tablespoons"] = "tablespoon",
        ["tsp"] = "teaspoon", ["teaspoons"] = "teaspoon",
        ["floz"] = "fluidounce", ["fl oz"] = "fluidounce",

        // Data
        ["b"] = "byte", ["bytes"] = "byte",
        ["kb"] = "kilobyte", ["kilobytes"] = "kilobyte",
        ["mb"] = "megabyte", ["megabytes"] = "megabyte",
        ["gb"] = "gigabyte", ["gigabytes"] = "gigabyte",
        ["tb"] = "terabyte", ["terabytes"] = "terabyte",

        // Speed
        ["mph"] = "milesperhour",
        ["kph"] = "kmh", ["km/h"] = "kmh", ["kmph"] = "kmh",
        ["m/s"] = "meterspersecond", ["mps"] = "meterspersecond",
        ["knot"] = "knot", ["knots"] = "knot", ["kn"] = "knot",
    };

    private static Dictionary<string, UnitFamily> BuildFamilies()
    {
        return new Dictionary<string, UnitFamily>
        {
            ["Length"] = new("Length", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["millimeter"] = new(0.001m, "mm"),
                ["centimeter"] = new(0.01m, "cm"),
                ["meter"] = new(1m, "m"),
                ["kilometer"] = new(1000m, "km"),
                ["inch"] = new(0.0254m, "in"),
                ["foot"] = new(0.3048m, "ft"),
                ["yard"] = new(0.9144m, "yd"),
                ["mile"] = new(1609.344m, "mi"),
            }),
            ["Weight"] = new("Weight", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["milligram"] = new(0.000001m, "mg"),
                ["gram"] = new(0.001m, "g"),
                ["kilogram"] = new(1m, "kg"),
                ["ounce"] = new(0.0283495m, "oz"),
                ["pound"] = new(0.453592m, "lb"),
                ["stone"] = new(6.35029m, "st"),
                ["tonne"] = new(1000m, "t"),
            }),
            ["Temperature"] = new("Temperature", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["celsius"] = new(1m, "C"),
                ["fahrenheit"] = new(1m, "F"),
                ["kelvin"] = new(1m, "K"),
            }),
            ["Volume"] = new("Volume", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["milliliter"] = new(0.001m, "mL"),
                ["liter"] = new(1m, "L"),
                ["gallon"] = new(3.78541m, "gal"),
                ["quart"] = new(0.946353m, "qt"),
                ["pint"] = new(0.473176m, "pt"),
                ["cup"] = new(0.236588m, "cup"),
                ["tablespoon"] = new(0.0147868m, "tbsp"),
                ["teaspoon"] = new(0.00492892m, "tsp"),
                ["fluidounce"] = new(0.0295735m, "fl oz"),
            }),
            ["Data"] = new("Data", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["byte"] = new(1m, "B"),
                ["kilobyte"] = new(1024m, "KB"),
                ["megabyte"] = new(1048576m, "MB"),
                ["gigabyte"] = new(1073741824m, "GB"),
                ["terabyte"] = new(1099511627776m, "TB"),
            }),
            ["Speed"] = new("Speed", new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["meterspersecond"] = new(1m, "m/s"),
                ["kmh"] = new(0.277778m, "km/h"),
                ["milesperhour"] = new(0.44704m, "mph"),
                ["knot"] = new(0.514444m, "kn"),
            }),
        };
    }

    [GeneratedRegex(@"^(?<value>\d+\.?\d*)\s*(?<from>[a-zA-Z/\s""]+?)\s+(?:in|to|as)\s+(?<to>[a-zA-Z/\s""]+?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ConversionPattern();

    private sealed record UnitDef(decimal ToBase, string Display);
    private sealed record UnitFamily(string Name, Dictionary<string, UnitDef> Units);
}
