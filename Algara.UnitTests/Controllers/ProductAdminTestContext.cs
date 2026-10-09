using System.Linq.Expressions;
using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace Algara.UnitTests.Controllers;

// Queries run over supplied rows; mutations use a mocked repository and never
// connect to SQL Server. Database constraints and MVC binding need HTTP/SQL tests.
internal sealed class ProductAdminTestContext : ShopDbContext
{
    public ProductAdminTestContext(Product[] products, Category[] categories, SubCategory[] subCategories)
        : base(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedProductUnitTest;Integrated Security=True;TrustServerCertificate=True")
            .Options)
    {
        Products = CreateSet(products);
        Categories = CreateSet(categories);
        SubCategories = CreateSet(subCategories);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Product controller tests must save through the mocked repository.");
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
