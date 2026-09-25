using System.Data.Common;

namespace VMS.Modules.Trips.Data;

/// <summary>Parameterised commands on a DbContext's own connection, for the one statement EF cannot express (the race-safe seed insert). Mirrors every other module's own helper of the same name — each module keeps its own, so no module depends on another's internals.</summary>
internal static class RawSql
{
    public static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
