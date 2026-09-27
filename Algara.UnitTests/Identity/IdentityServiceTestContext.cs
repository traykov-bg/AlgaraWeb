using System.Linq.Expressions;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace Algara.UnitTests.Identity;

// LINQ reads use supplied rows; EF still tracks real entity changes. Saves are
// observed here and never reach SQL Server. SQL constraints need integration tests.
internal sealed class IdentityServiceTestContext : IdentityDbContext
{
    public int SaveCount { get; private set; }
    public Action? OnSave { get; set; }

    public IdentityServiceTestContext(
        ApplicationUser[]? users = null,
        ApplicationRole[]? roles = null,
        UserRole[]? assignments = null)
        : base(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedIdentityUnitTest;Integrated Security=True;TrustServerCertificate=True")
            .Options)
    {
        users ??= [];
        roles ??= [];
        assignments ??= [];
        AttachRange(users);
        AttachRange(roles);
        AttachRange(assignments);
        Users = CreateSet(users);
        Roles = CreateSet(roles);
        UserRoles = CreateSet(assignments);
        UserConsents = CreateSet(Array.Empty<UserConsent>());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        ChangeTracker.DetectChanges();
        OnSave?.Invoke();
        ChangeTracker.AcceptAllChanges();
        return Task.FromResult(1);
    }

    private DbSet<TEntity> CreateSet<TEntity>(IEnumerable<TEntity> rows) where TEntity : class
    {
        var query = rows.AsQueryable();
        var set = new Mock<DbSet<TEntity>>();
        set.As<IQueryable<TEntity>>().SetupGet(s => s.Provider).Returns(new AsyncQueryProvider(query.Provider));
        set.As<IQueryable<TEntity>>().SetupGet(s => s.Expression).Returns(query.Expression);
        set.As<IQueryable<TEntity>>().SetupGet(s => s.ElementType).Returns(query.ElementType);
        set.As<IQueryable<TEntity>>().Setup(s => s.GetEnumerator()).Returns(() => query.GetEnumerator());
        set.Setup(s => s.Add(It.IsAny<TEntity>())).Returns((TEntity entity) => Add(entity));
        set.Setup(s => s.Remove(It.IsAny<TEntity>())).Returns((TEntity entity) => Remove(entity));
        set.Setup(s => s.AddRange(It.IsAny<IEnumerable<TEntity>>()))
            .Callback((IEnumerable<TEntity> entities) => AddRange(entities));
        return set.Object;
    }

    private sealed class AsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => inner.CreateQuery(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => inner.CreateQuery<TElement>(expression);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = inner.Execute(expression);
            var resultType = typeof(TResult).GetGenericArguments().Single();
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType).Invoke(null, [value])!;
        }
    }
}
