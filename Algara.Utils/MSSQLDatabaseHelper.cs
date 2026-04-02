namespace Algara.Utils
{
    using Dapper;
    using Microsoft.Data.SqlClient;
    using System.Data;

    public class MSSQLDatabaseHelper : DatabaseHelperBase
    {
        public MSSQLDatabaseHelper(string connectionString) : base(connectionString) { }

        private IDbConnection CreateConnection() => new SqlConnection(ConnectionString);

        public override async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
        {
            using var connection = CreateConnection();
            return await connection.QueryAsync<T>(sql, parameters);
        }

        public override async Task<int> ExecuteAsync(string sql, object? parameters = null)
        {
            using var connection = CreateConnection();
            return await connection.ExecuteAsync(sql, parameters);
        }

        public override async Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null) where T : default
        {
            using var connection = CreateConnection();
            return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters);
        }
    }
}
