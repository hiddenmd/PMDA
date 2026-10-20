using System.Globalization;
using System.Text.RegularExpressions;
using PMDA.Formats.Decoding;

namespace PMDA.Formats.Recipes;

/// <summary>
/// A field scaled and offset: <c>name</c>, <c>name*k</c>, <c>name/k</c>, <c>name*k+b</c>, <c>name-b</c>; or a constant
/// number (an empty <see cref="Field"/>).
/// </summary>
public sealed partial record FieldExpr(string Field, double Multiply, double Add)
{
    public static FieldExpr Parse(string text)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
            return new FieldExpr("", 0, constant);
        var m = Pattern().Match(text.Replace(" ", "", StringComparison.Ordinal));
        if (!m.Success)
            throw new FormatException($"'{text}' is not a field expression (name, name*k, name/k, name*k+b).");
        var multiply = 1.0;
        if (m.Groups["op"].Success)
        {
            var k = double.Parse(m.Groups["k"].Value, CultureInfo.InvariantCulture);
            multiply = m.Groups["op"].Value == "/" ? 1 / k : k;
        }
        var add = m.Groups["b"].Success ? double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups["sign"].Value == "-")
            add = -add;
        return new FieldExpr(m.Groups["name"].Value, multiply, add);
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:(?<op>[*/])(?<k>[0-9.]+))?(?:(?<sign>[+-])(?<b>[0-9.]+))?$")]
    private static partial Regex Pattern();

    public Bound? Bind(MessagePlan plan) =>
        Field.Length == 0 ? new Bound(-1, 0, Add) : plan.IndexOf(Field) is var i and >= 0 ? new Bound(i, Multiply, Add) : null;

    public readonly record struct Bound(int Column, double Multiply, double Add)
    {
        public double? Evaluate(object?[] values) =>
            Column < 0 ? Add
            : values[Column] is { } v and not string ? Convert.ToDouble(v, CultureInfo.InvariantCulture) * Multiply + Add
            : null;
    }
}

/// <summary>
/// A row filter: <c>field==v</c>, <c>!=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c>; several joined with
/// <c>&amp;&amp;</c> must all hold.
/// </summary>
public sealed partial record Condition(string Field, string Op, double Value)
{
    /// <summary>Binds every <c>&amp;&amp;</c> part; null when a part names a field the message lacks.</summary>
    public static Func<object?[], bool>? BindAll(string text, MessagePlan plan)
    {
        var parts = text.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Parse(p).Bind(plan))
            .ToList();
        if (parts.Any(p => p is null))
            return null;
        return values => parts.All(p => p!(values));
    }

    public static Condition Parse(string text)
    {
        var m = Pattern().Match(text.Replace(" ", "", StringComparison.Ordinal));
        if (!m.Success)
            throw new FormatException($"'{text}' is not a condition (field==value, !=, >, <, >=, <=).");
        return new Condition(m.Groups[1].Value, m.Groups[2].Value, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)(==|!=|>=|<=|>|<)(-?[0-9.]+)$")]
    private static partial Regex Pattern();

    public Func<object?[], bool>? Bind(MessagePlan plan)
    {
        var i = plan.IndexOf(Field);
        if (i < 0)
            return null;
        return values =>
        {
            if (values[i] is not { } v || v is string)
                return false;
            var d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return Op switch
            {
                "==" => d == Value,
                "!=" => d != Value,
                ">" => d > Value,
                "<" => d < Value,
                ">=" => d >= Value,
                _ => d <= Value,
            };
        };
    }
}
