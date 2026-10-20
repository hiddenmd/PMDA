using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PMDA.Formats.Dialect;

/// <summary>
/// Reads the annotated-C dialect of vendor recorder headers. It is not a C compiler: vendor files repeat
/// enumerators, comment out enums they still reference and carry their meaning in comment tags, so the parser
/// accepts all of that and reports what it could not read instead of failing.
/// </summary>
public static partial class HeaderParser
{
    public static HeaderModel Parse(string text)
    {
        var model = new HeaderModel();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var active = Tokenizer.Tokenize(lines, out var comments);
        new Reader(active, comments, model, enumsOnly: false).ReadAll();

        // Enums that only exist in commented-out code: vendors reference them from <Enum> tags.
        var commented = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            commented[i] = CommentedCode(lines[i]) ?? "";
        var hidden = Tokenizer.Tokenize(commented, out var hiddenComments);
        new Reader(hidden, hiddenComments, model, enumsOnly: true).ReadAll();

        Check(model);
        return model;
    }

    /// <summary>The text after <c>//</c> when the whole line is a comment, else null.</summary>
    private static string? CommentedCode(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal) ? trimmed[2..] : null;
    }

    private static void Check(HeaderModel model)
    {
        var ids = new Dictionary<int, StructDecl>();
        foreach (var s in model.Structs)
        {
            if (s.Id is { } id && !ids.TryAdd(id, s))
                model.Diagnostics.Add(new(s.Line, Severity.Warning, $"<ID>{id} is used by both {ids[id].DisplayName} and {s.DisplayName}; {ids[id].DisplayName} is used."));
            foreach (var f in s.Fields)
                foreach (var tag in new[] { "Enum", "ColorEnum" })
                    if (f.Tags.Value(tag) is { } e && !model.Enums.ContainsKey(e))
                        model.Diagnostics.Add(new(f.Line, Severity.Warning, $"{s.DisplayName}.{FieldLabel(f)}: <{tag}>{e} is not defined anywhere; the values show as numbers."));
        }
    }

    internal static string FieldLabel(FieldDecl f) => f.IsAnonymous ? "(padding)" : f.Name;

    internal enum Kind { Ident, Number, Punct, Preproc }

    internal readonly record struct Token(Kind Kind, string Text, int Line);

    private static class Tokenizer
    {
        /// <summary>Code tokens, plus every line's comment text (1-based line numbers).</summary>
        public static List<Token> Tokenize(string[] lines, out Dictionary<int, string> comments)
        {
            var tokens = new List<Token>();
            comments = new Dictionary<int, string>();
            var inBlock = false;
            for (var n = 0; n < lines.Length; n++)
            {
                var line = lines[n];
                var lineNo = n + 1;
                var i = 0;
                if (!inBlock && line.TrimStart().StartsWith('#'))
                {
                    tokens.Add(new Token(Kind.Preproc, line.Trim(), lineNo));
                    continue;
                }
                while (i < line.Length)
                {
                    if (inBlock)
                    {
                        var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                        if (end < 0)
                            break;
                        inBlock = false;
                        i = end + 2;
                        continue;
                    }
                    var c = line[i];
                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                    }
                    else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        comments[lineNo] = comments.TryGetValue(lineNo, out var prev) ? prev + " " + line[(i + 2)..] : line[(i + 2)..];
                        break;
                    }
                    else if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                    {
                        inBlock = true;
                        i += 2;
                    }
                    else if (char.IsLetter(c) || c == '_')
                    {
                        var start = i;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                            i++;
                        tokens.Add(new Token(Kind.Ident, line[start..i], lineNo));
                    }
                    else if (char.IsDigit(c))
                    {
                        var start = i;
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '.'))
                            i++;
                        tokens.Add(new Token(Kind.Number, line[start..i], lineNo));
                    }
                    else
                    {
                        tokens.Add(new Token(Kind.Punct, c.ToString(), lineNo));
                        i++;
                    }
                }
            }
            return tokens;
        }
    }

    private sealed partial class Reader(List<Token> tokens, Dictionary<int, string> comments, HeaderModel model, bool enumsOnly)
    {
        private int _i;

        private bool End => _i >= tokens.Count;
        private Token Peek(int ahead = 0) => _i + ahead < tokens.Count ? tokens[_i + ahead] : new Token(Kind.Punct, "", tokens.Count > 0 ? tokens[^1].Line : 0);
        private Token Next() => tokens[_i++];
        private bool IsPunct(string p, int ahead = 0) => Peek(ahead) is { Kind: Kind.Punct } t && t.Text == p;
        private bool IsWord(string w, int ahead = 0) => Peek(ahead) is { Kind: Kind.Ident } t && t.Text == w;

        private Tags TagsOn(int line) => comments.TryGetValue(line, out var c) ? Tags.Parse(c) : Tags.Empty;

        private Tags TagsBetween(int fromLine, int toLine)
        {
            var tags = Tags.Empty;
            for (var l = fromLine; l <= toLine; l++)
                tags = tags.Merge(TagsOn(l));
            return tags;
        }

        public void ReadAll()
        {
            while (!End)
            {
                var start = _i;
                try
                {
                    if (enumsOnly)
                    {
                        if (IsWord("enum") && (IsPunct("{", 1) || IsPunct("{", 2)))
                            ReadEnum(alias: false);
                        else
                            _i++;
                        continue;
                    }
                    var t = Peek();
                    if (t.Kind == Kind.Preproc)
                    {
                        Preprocessor(Next());
                    }
                    else if (IsWord("typedef"))
                    {
                        Next();
                        ReadTypedef();
                    }
                    else if (IsWord("struct") || IsWord("union"))
                    {
                        ReadStruct(typedef: false);
                    }
                    else if (IsWord("enum"))
                    {
                        ReadEnum(alias: false);
                    }
                    else if (IsPunct(";"))
                    {
                        Next();
                    }
                    else
                    {
                        Report(t.Line, Severity.Warning, $"Skipped unexpected '{t.Text}'.");
                        SkipStatement();
                    }
                }
                catch (FormatException ex)
                {
                    if (enumsOnly)
                    {
                        // Prose in commented-out code: look for the next enum from just after this one.
                        _i = start + 1;
                        continue;
                    }
                    Report(Peek().Line, Severity.Error, ex.Message);
                    if (_i == start)
                        _i++;
                    SkipStatement();
                }
            }
        }

        private void Report(int line, Severity severity, string message) => model.Diagnostics.Add(new(line, severity, message));

        private void Preprocessor(Token t)
        {
            var m = PackPragma().Match(t.Text);
            if (m.Success)
                model.PackHint = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        [GeneratedRegex(@"#\s*pragma\s+pack\s*\(\s*(?:push\s*,\s*)?(\d+)\s*\)")]
        private static partial Regex PackPragma();

        /// <summary>Skips to the end of the current statement, past any braces it opens.</summary>
        private void SkipStatement()
        {
            var depth = 0;
            while (!End)
            {
                var t = Next();
                if (t.Kind != Kind.Punct)
                    continue;
                if (t.Text == "{")
                    depth++;
                else if (t.Text == "}")
                    depth = Math.Max(0, depth - 1);
                else if (t.Text == ";" && depth == 0)
                    return;
            }
        }

        private Token Expect(string punct)
        {
            if (!IsPunct(punct))
                throw new FormatException($"Expected '{punct}' but found '{Peek().Text}'.");
            return Next();
        }

        private void ReadTypedef()
        {
            if (IsWord("struct") || IsWord("union"))
            {
                ReadStruct(typedef: true);
                return;
            }
            if (IsWord("enum"))
            {
                ReadEnum(alias: true);
                return;
            }
            var words = new List<Token>();
            while (!End && !IsPunct(";"))
                words.Add(Next());
            Expect(";");
            if (words.Count < 2 || words[^1].Kind != Kind.Ident)
            {
                Report(words.FirstOrDefault().Line, Severity.Warning, "Skipped a typedef that is not a simple type alias.");
                return;
            }
            model.Typedefs[words[^1].Text] = string.Join(' ', words.Take(words.Count - 1).Select(w => w.Text));
        }

        private void ReadStruct(bool typedef)
        {
            var keyword = Next();
            if (keyword.Text == "union")
                throw new FormatException("Unions are not supported; the union and what follows it in its statement are skipped.");
            var name = Peek().Kind == Kind.Ident ? Next().Text : "";
            if (!IsPunct("{"))
            {
                // A forward declaration or a variable of struct type at file level: nothing to describe.
                SkipStatement();
                return;
            }
            var open = Next();
            var fields = new List<FieldDecl>();
            while (!End && !IsPunct("}"))
            {
                if (IsPunct(";"))
                {
                    Next();
                    continue;
                }
                fields.AddRange(ReadField(name));
            }
            var close = Expect("}");
            string? alias = null;
            while (!End && !IsPunct(";"))
            {
                var t = Next();
                if (t.Kind == Kind.Ident && alias is null)
                    alias = t.Text;
            }
            var semicolon = End ? close : Expect(";");
            if (name.Length == 0 && alias is null)
            {
                Report(keyword.Line, Severity.Warning, "Skipped a struct with no name.");
                return;
            }
            // Struct tags sit on the "struct X" line, between it and "{", or after the closing "} ALIAS;".
            var tags = TagsBetween(keyword.Line, open.Line);
            if (semicolon.Line != (fields.Count > 0 ? fields[^1].Line : open.Line))
                tags = tags.Merge(TagsOn(semicolon.Line));
            var decl = new StructDecl(name, typedef ? alias : null, fields, tags, keyword.Line);
            var existing = model.Structs.FindIndex(s => (s.Name.Length > 0 && s.Name == decl.Name) || (s.Alias is not null && s.Alias == decl.Alias));
            if (existing >= 0)
            {
                Report(keyword.Line, Severity.Warning, $"struct {decl.DisplayName} is declared twice; the later one is used.");
                model.Structs[existing] = decl;
            }
            else
            {
                model.Structs.Add(decl);
            }
        }

        private IEnumerable<FieldDecl> ReadField(string structName)
        {
            var decl = new List<Token>();
            var depth = 0;
            while (!End)
            {
                if (IsPunct("{"))
                    throw new FormatException($"Nested struct or union definitions inside {structName} are not supported.");
                if (IsPunct("[")) depth++;
                if (IsPunct("]")) depth--;
                if (depth == 0 && IsPunct(";"))
                    break;
                if (depth == 0 && IsPunct("}"))
                    throw new FormatException($"Missing ';' before '}}' in {structName}.");
                decl.Add(Next());
            }
            var end = Expect(";");
            var tags = TagsOn(end.Line);

            // "int a, b;" declares two fields of one type.
            var parts = Split(decl);
            var first = Declarator(parts[0], typeWords: null, structName, end.Line, tags, out var typeWords);
            var result = new List<FieldDecl> { first };
            foreach (var part in parts.Skip(1))
                result.Add(Declarator(part, typeWords, structName, end.Line, tags, out _));
            return result;
        }

        private static List<List<Token>> Split(List<Token> decl)
        {
            var parts = new List<List<Token>> { new() };
            var depth = 0;
            foreach (var t in decl)
            {
                if (t is { Kind: Kind.Punct, Text: "[" }) depth++;
                if (t is { Kind: Kind.Punct, Text: "]" }) depth--;
                if (depth == 0 && t is { Kind: Kind.Punct, Text: "," })
                    parts.Add(new());
                else
                    parts[^1].Add(t);
            }
            return parts;
        }

        private static readonly HashSet<string> TypeKeywords = new(StringComparer.Ordinal)
        {
            "unsigned", "signed", "long", "short", "int", "char", "float", "double", "struct", "enum",
            "const", "volatile", "bool", "_Bool", "__int8", "__int16", "__int32", "__int64",
        };

        private FieldDecl Declarator(List<Token> tokens, string? typeWords, string structName, int line, Tags tags, out string resolvedType)
        {
            int? bits = null;
            var colon = tokens.FindIndex(t => t is { Kind: Kind.Punct, Text: ":" });
            if (colon >= 0)
            {
                if (colon + 1 >= tokens.Count || !TryNumber(tokens[colon + 1].Text, out var w))
                    throw new FormatException($"Bad bit width in {structName}.");
                bits = (int)w;
                tokens = tokens[..colon];
            }

            var length = 0;
            var bracket = tokens.FindIndex(t => t is { Kind: Kind.Punct, Text: "[" });
            if (bracket >= 0)
            {
                length = 1;
                for (var k = bracket; k < tokens.Count; k++)
                {
                    if (tokens[k] is not { Kind: Kind.Punct, Text: "[" })
                        continue;
                    if (k + 2 >= tokens.Count || !TryNumber(tokens[k + 1].Text, out var n) || tokens[k + 2].Text != "]")
                        throw new FormatException($"Array sizes in {structName} must be numbers.");
                    length *= (int)n;
                }
                tokens = tokens[..bracket];
            }

            if (tokens.Any(t => t is { Kind: Kind.Punct, Text: "*" }))
                throw new FormatException($"Pointers in {structName} cannot be stored in a recording.");

            var words = tokens.Where(t => t.Kind == Kind.Ident).Select(t => t.Text).ToList();
            string name;
            if (typeWords is not null)
            {
                name = words.LastOrDefault() ?? "";
                resolvedType = typeWords;
            }
            else
            {
                // The last word is the field name unless it is still part of the type ("unsigned : 8").
                var lastIsName = words.Count >= 2 && !TypeKeywords.Contains(words[^1]);
                name = lastIsName ? words[^1] : "";
                resolvedType = string.Join(' ', lastIsName ? words.Take(words.Count - 1) : words);
                if (resolvedType.Length == 0)
                    throw new FormatException($"A field in {structName} has no type.");
            }
            return new FieldDecl(name, resolvedType, bits, length, tags, line);
        }

        private void ReadEnum(bool alias)
        {
            var keyword = Next();
            var name = Peek().Kind == Kind.Ident ? Next().Text : "";
            if (!IsPunct("{"))
            {
                SkipStatement();
                return;
            }
            Next();
            var members = new List<EnumMember>();
            long next = 0;
            var byName = new Dictionary<string, long>(StringComparer.Ordinal);
            while (!End && !IsPunct("}"))
            {
                if (IsPunct(","))
                {
                    Next();
                    continue;
                }
                var t = Next();
                if (t.Kind is not (Kind.Ident or Kind.Number))
                {
                    if (enumsOnly)
                        throw new FormatException("not an enum");
                    throw new FormatException($"Unexpected '{t.Text}' in enum {name}.");
                }
                var value = next;
                if (t.Kind == Kind.Number && TryNumber(t.Text, out var literal))
                    value = literal; // "enum TRUE_FALSE { 0, 1 }": the number is the name and the value
                if (IsPunct("="))
                {
                    Next();
                    var negative = false;
                    if (IsPunct("-"))
                    {
                        Next();
                        negative = true;
                    }
                    var v = Next();
                    if (TryNumber(v.Text, out var number))
                        value = negative ? -number : number;
                    else if (byName.TryGetValue(v.Text, out var earlier))
                        value = negative ? -earlier : earlier;
                    else
                        throw new FormatException($"Enum {name}: value '{v.Text}' is not a number.");
                }
                var comment = comments.TryGetValue(t.Line, out var c) ? c.Trim() : null;
                members.Add(new EnumMember(t.Text, value, comment));
                byName.TryAdd(t.Text, value);
                next = value + 1;
            }
            Expect("}");
            string? typedefName = null;
            while (!End && !IsPunct(";") && !IsWord("enum") && !IsWord("struct") && !IsWord("typedef"))
            {
                var t = Next();
                if (t.Kind == Kind.Ident)
                    typedefName ??= t.Text;
            }
            if (IsPunct(";"))
                Next();

            var key = name.Length > 0 ? name : typedefName;
            if (key is null)
                return;
            var def = new EnumDef(key, members, enumsOnly, keyword.Line);
            if (enumsOnly)
            {
                model.Enums.TryAdd(key, def); // a live definition always wins
                return;
            }
            if (!model.Enums.TryAdd(key, def))
                Report(keyword.Line, Severity.Info, $"enum {key} is defined twice; the later one is used.");
            model.Enums[key] = def;
            if (alias && typedefName is not null && typedefName != key)
                model.Typedefs[typedefName] = "int";
        }

        internal static bool TryNumber(string text, out long value)
        {
            var s = text.TrimEnd('u', 'U', 'l', 'L');
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>For tests and reports: the dialect text of a model's struct, one field per line.</summary>
    public static string Describe(StructDecl s)
    {
        var sb = new StringBuilder();
        sb.Append("struct ").Append(s.DisplayName).AppendLine();
        foreach (var f in s.Fields)
            sb.Append("  ").Append(f.TypeName).Append(' ').Append(FieldLabel(f))
                .Append(f.IsArray ? $"[{f.ArrayLength}]" : "")
                .Append(f.BitWidth is { } b ? $" : {b}" : "").AppendLine();
        return sb.ToString();
    }
}
