using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

/// <summary>
/// Views, stored procedures, functions and triggers are recreated by replaying the exact CREATE
/// statement text sys.sql_modules.definition already captured - no reconstruction needed.
/// </summary>
public static class RawDefinitionScriptGenerator
{
    public static string GenerateDrop(ObjectDefinition obj)
    {
        var keyword = obj.Ref.Kind switch
        {
            DatabaseObjectKind.View => "VIEW",
            DatabaseObjectKind.StoredProcedure => "PROCEDURE",
            DatabaseObjectKind.Function => "FUNCTION",
            DatabaseObjectKind.Trigger => "TRIGGER",
            _ => throw new ArgumentOutOfRangeException(nameof(obj), obj.Ref.Kind, "Not a programmable object kind.")
        };

        return $"DROP {keyword} {obj.Ref.Identifier.Quoted};";
    }

    public static string GenerateCreate(ObjectDefinition obj)
    {
        // The captured text is replayed byte-for-byte whenever possible (only trimmed for the purpose of
        // checking for a trailing ';', never in the text actually sent) - SQL Server stores whatever text
        // it is given verbatim in sys.sql_modules.definition, so trimming here would make the recreated
        // object's definition differ from the original's and show up as a false structural diff.
        var script = obj.DefinitionScript;
        return script.TrimEnd().EndsWith(';') ? script : script + ";";
    }
}
