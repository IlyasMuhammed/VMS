using System.Data.Common;

namespace VMS.Modules.Core.Data;

/// <summary>Parameterised commands on a DbContext's own connection, for the few statements EF cannot express (locking hints, MERGE).</summary>
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
