using System.Text.RegularExpressions;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

/// <summary>
/// Recreates views/procedures/functions/triggers by replaying the captured sys.sql_modules.definition
/// text verbatim, rewriting only the header (CREATE/ALTER keyword + schema-qualified name) when it no
/// longer matches the object's current sys.objects identity:
///
///  - After sp_rename, sys.objects.name changes but sys.sql_modules.definition still says "CREATE VIEW
///    old_name ...", so replaying it verbatim would silently recreate the object under the old name.
///  - If the object's last modification was an ALTER, the definition text starts with "ALTER VIEW ...",
///    which fails against a just-dropped object (ALTER requires it to already exist).
///
/// The header is left untouched when it already matches, so the recreated definition stays
/// byte-for-byte identical and doesn't show up as a false structural diff. Leading "--"/"/* */" comments
/// and SET ANSI_NULLS/QUOTED_IDENTIFIER statements captured before the header are preserved verbatim.
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
        // Trimming below is only used to check for a trailing ';', never applied to the text actually
        // sent - SQL Server stores definitions verbatim, so trimming here would cause a false diff later.
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
