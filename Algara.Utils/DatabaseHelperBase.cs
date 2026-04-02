namespace Algara.Utils
{
    public abstract class DatabaseHelperBase : IDatabaseHelper
    {
        protected readonly string ConnectionString;

        protected DatabaseHelperBase(string connectionString)
        {
            ConnectionString = connectionString;
        }
        public async Task EnsureTablesExist()
        {
            string checkTableQuery = "SELECT count(*) FROM sys.tables WHERE name = 'Products'";
            int tableExists = await QuerySingleAsync<int>(checkTableQuery);

            if (tableExists == 0)
            {
                string createTableQuery = @"
                CREATE TABLE Products (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(255) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    Price DECIMAL(18,2) NOT NULL,
                    ImageUrl NVARCHAR(500) NULL,
                    IsCustomizable BIT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
                )";

                await ExecuteAsync(createTableQuery);
            }
        }
        public abstract Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null);
        public abstract Task<int> ExecuteAsync(string sql, object? parameters = null);
        public abstract Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null);
    }
}
