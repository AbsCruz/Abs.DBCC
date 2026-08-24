using System.Text.RegularExpressions;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

/// <summary>
/// Views, stored procedures, functions and triggers are recreated by replaying the exact CREATE
/// statement text sys.sql_modules.definition already captured - no reconstruction needed, with one
/// exception: the object's header (its CREATE/ALTER keyword and schema-qualified name) is rewritten
/// to match its <em>current</em> sys.objects identity whenever that differs from what the stored text
/// says, before replaying it. Two real-world cases make this necessary:
///
///  - The object was renamed with sp_rename at some point: sp_rename updates sys.objects.name but
///    never touches sys.sql_modules.definition, so the stored text keeps saying "CREATE VIEW
///    old_name ..." forever after. Replaying it verbatim after a DROP recreates an object under the
///    *old* name, silently - the very next step (e.g. an index or a permission scoped to the current
///    name) then fails with "object not found", because the name it expects was never actually created.
///  - The object's last modification used ALTER rather than CREATE: sys.sql_modules.definition simply
///    stores whatever statement was last run, verbatim. Replaying "ALTER VIEW ..." against an object
///    that was just DROPped fails outright (ALTER requires the object to already exist).
///
/// Both are only rewritten when actually detected (name mismatch, or a non-CREATE keyword) - the
/// common case where the captured text already matches reality is replayed completely untouched, so a
/// recreated object's own captured definition still matches the original byte-for-byte and does not
/// show up as a false structural diff. Any leading comments (SQL Server allows "--"/"/* */" comments,
/// and the ANSI_NULLS/QUOTED_IDENTIFIER SET statements, before the CREATE/ALTER statement in the same
/// batch, and captures them as part of sys.sql_modules.definition too) are matched past and preserved
/// verbatim in front of a rewritten header, rather than accidentally requiring the header to start at
/// the very beginning of the text.
/// </summary>
public static class RawDefinitionScriptGenerator
{
    private static readonly Regex HeaderPattern = new(
        """\A(?<trivia>(?:\s|--[^\r\n]*|/\*.*?\*/|SET\s+(?:ANSI_NULLS|QUOTED_IDENTIFIER)\s+(?:ON|OFF)\s*;?)*)(?<keyword>CREATE|ALTER)\s+(?<objtype>VIEW|PROCEDURE|PROC|FUNCTION|TRIGGER)\s+(?<name>(?:\[[^\]]+\]|"[^"]+"|[^\s.(]+)(?:\s*\.\s*(?:\[[^\]]+\]|"[^"]+"|[^\s.(]+))?)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static string GenerateDrop(ObjectDefinition obj)
    {
        return $"DROP {KeywordFor(obj.Ref.Kind)} {obj.Ref.Identifier.Quoted};";
    }

    public static string GenerateCreate(ObjectDefinition obj)
    {
        // The captured text is replayed byte-for-byte whenever possible (only trimmed for the purpose of
        // checking for a trailing ';', never in the text actually sent) - SQL Server stores whatever text
        // it is given verbatim in sys.sql_modules.definition, so trimming here would make the recreated
        // object's definition differ from the original's and show up as a false structural diff.
        var script = obj.DefinitionScript;
        var header = HeaderPattern.Match(script);

        if (header.Success && NeedsHeaderRewrite(header, obj.Ref))
        {
            var rewrittenHeader = $"{header.Groups["trivia"].Value}CREATE {KeywordFor(obj.Ref.Kind)} {obj.Ref.Identifier.Quoted}";
            script = script[..header.Index] + rewrittenHeader + script[(header.Index + header.Length)..];
        }

        return script.TrimEnd().EndsWith(';') ? script : script + ";";
    }

    private static string KeywordFor(DatabaseObjectKind kind) => kind switch
    {
        DatabaseObjectKind.View => "VIEW",
        DatabaseObjectKind.StoredProcedure => "PROCEDURE",
        DatabaseObjectKind.Function => "FUNCTION",
        DatabaseObjectKind.Trigger => "TRIGGER",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a programmable object kind.")
    };

    private static bool NeedsHeaderRewrite(Match header, ObjectRef currentRef)
    {
        if (!string.Equals(header.Groups["keyword"].Value, "CREATE", StringComparison.OrdinalIgnoreCase))
            return true;

        var (schema, name) = SplitQualifiedName(header.Groups["name"].Value);
        if (!string.Equals(name, currentRef.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        return schema is not null && !string.Equals(schema, currentRef.SchemaName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits an (optionally bracket/quote-delimited, optionally schema-qualified) name captured by <see cref="HeaderPattern"/>.</summary>
    private static (string? Schema, string Name) SplitQualifiedName(string qualified)
    {
        qualified = qualified.Trim();

        var firstTokenEnd = qualified[0] switch
        {
            '[' => qualified.IndexOf(']'),
            '"' => qualified.IndexOf('"', 1),
            _ => -1
        };

        var dotIndex = qualified.IndexOf('.', firstTokenEnd + 1);
        if (dotIndex < 0)
            return (null, Unquote(qualified));

        return (Unquote(qualified[..dotIndex]), Unquote(qualified[(dotIndex + 1)..]));
    }

    private static string Unquote(string part)
    {
        part = part.Trim();

        if (part.Length >= 2 && part[0] == '[' && part[^1] == ']')
            return part[1..^1].Replace("]]", "]");

        if (part.Length >= 2 && part[0] == '"' && part[^1] == '"')
            return part[1..^1].Replace("\"\"", "\"");

        return part;
    }
}
