using System.Linq.Expressions;
using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace Algara.UnitTests.Controllers;

// Queries run over supplied rows and EF tracks mutations, but SaveChanges never
// connects to SQL Server. Database constraints and MVC binding need HTTP/SQL tests.
internal sealed class PromotionTestContext : ShopDbContext
{
    public int SaveCount { get; private set; }

    public PromotionTestContext(Product[] products, Promotion[]? promotions = null)
        : base(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedPromotionUnitTest;Integrated Security=True;TrustServerCertificate=True")
            .Options)
    {
        promotions ??= [];
        AttachRange(products);
        AttachRange(promotions);
        Products = CreateSet(products);
        Promotions = CreateSet(promotions);
        ProductPromotions = CreateSet(promotions.SelectMany(p => p.ProductPromotions));
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<Promotion>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(p => p.N).CurrentValue = 900 + SaveCount;
            entry.Property(p => p.N).IsTemporary = false;
        }
        ChangeTracker.AcceptAllChanges();
        return Task.FromResult(1);
    }

    private DbSet<TEntity> CreateSet<TEntity>(IEnumerable<TEntity> rows) where TEntity : class
    {
        var query = new AsyncEnumerable<TEntity>(rows);
        var set = new Mock<DbSet<TEntity>>();
        set.As<IQueryable<TEntity>>().SetupGet(s => s.Provider).Returns(((IQueryable<TEntity>)query).Provider);
        set.As<IQueryable<TEntity>>().SetupGet(s => s.Expression).Returns(((IQueryable<TEntity>)query).Expression);
        set.As<IQueryable<TEntity>>().SetupGet(s => s.ElementType).Returns(typeof(TEntity));
        set.As<IQueryable<TEntity>>().Setup(s => s.GetEnumerator()).Returns(() => ((IEnumerable<TEntity>)query).GetEnumerator());
        set.As<IAsyncEnumerable<TEntity>>().Setup(s => s.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => query.GetAsyncEnumerator(token));
        set.Setup(s => s.Add(It.IsAny<TEntity>())).Returns((TEntity entity) => Add(entity));
        set.Setup(s => s.Remove(It.IsAny<TEntity>())).Returns((TEntity entity) => Remove(entity));
        return set.Object;
    }

    private sealed class AsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncEnumerable(IEnumerable<T> items) : base(items) { }
        public AsyncEnumerable(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new AsyncQueryProvider(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new AsyncEnumerator<T>(this.AsEnumerable().GetEnumerator(), cancellationToken);
    }

    private sealed class AsyncEnumerator<T>(IEnumerator<T> inner, CancellationToken token) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
        public ValueTask<bool> MoveNextAsync()
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(inner.MoveNext());
        }
    }

    private sealed class AsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = expression.Type.GetGenericArguments().Single();
            return (IQueryable)Activator.CreateInstance(typeof(AsyncEnumerable<>).MakeGenericType(elementType), expression)!;
        }
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new AsyncEnumerable<TElement>(expression);
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
