using System.Linq.Expressions;
using System.Security.Claims;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;

namespace Algara.UnitTests.Controllers;

// Use the real access service and matching database facts. A cookie role claim
// alone deliberately cannot authorize direct controller business-rule tests.
internal sealed class StaffAccessTestContext : IdentityDbContext
{
    public List<RolePermission> Grants { get; } = [];
    public List<UserRole> Assignments { get; } = [];
    public ApplicationUser PrimaryUser { get; }

    public StaffAccessTestContext(string roleName = StaffRoles.Admin, IEnumerable<string>? grants = null,
        bool active = true, bool sessionActive = true) : base(new DbContextOptions<IdentityDbContext>())
    {
        var administrators = new[]
        {
            new ApplicationUser { N = 1, Id = "admin", UserName = "admin@example.test", IsActive = active, SecurityStamp = "stamp-admin" },
            new ApplicationUser { N = 2, Id = "other-admin", UserName = "other-admin@example.test", IsActive = true, SecurityStamp = "stamp-other-admin" }
        };
        PrimaryUser = administrators[0];
        var role = new ApplicationRole { N = 1, Name = roleName };
        Users = CreateSet(administrators);
        Roles = CreateSet(new[] { role });
        Assignments.AddRange(administrators.Select(u => new UserRole { UserN = u.N, RoleN = role.N, User = u, Role = role }));
        UserRoles = CreateSet(Assignments);
        UserSessions = CreateSet(administrators.Select(u => new UserSession { UserN = u.N, SessionId = "session-" + u.Id, IsActive = sessionActive, User = u }));
        Grants.AddRange((grants ?? []).Select(k => new RolePermission { RoleN = role.N, PermissionKey = k, Role = role }));
        RolePermissions = CreateSet(Grants);
    }

    public IServiceCollection AddServices(IServiceCollection services)
    {
        var urls = new Mock<IUrlHelperFactory>();
        urls.Setup(f => f.GetUrlHelper(It.IsAny<ActionContext>())).Returns(Mock.Of<IUrlHelper>());
        return services.AddSingleton(urls.Object).AddSingleton(new StaffAccessService(this));
    }

    public static ClaimsPrincipal Principal(string id = "admin") => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, id), new Claim("SecurityStamp", "stamp-" + id),
        new Claim("SessionId", "session-" + id)
    }, "Test"));

    private static DbSet<T> CreateSet<T>(IEnumerable<T> rows) where T : class
    {
        var query = new AsyncEnumerable<T>(rows);
        var set = new Mock<DbSet<T>>();
        set.As<IQueryable<T>>().SetupGet(s => s.Provider).Returns(((IQueryable<T>)query).Provider);
        set.As<IQueryable<T>>().SetupGet(s => s.Expression).Returns(((IQueryable<T>)query).Expression);
        set.As<IQueryable<T>>().SetupGet(s => s.ElementType).Returns(typeof(T));
        set.As<IQueryable<T>>().Setup(s => s.GetEnumerator()).Returns(() => ((IEnumerable<T>)query).GetEnumerator());
        set.As<IAsyncEnumerable<T>>().Setup(s => s.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => query.GetAsyncEnumerator(token));
        return set.Object;
    }

    private sealed class AsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncEnumerable(IEnumerable<T> items) : base(items) { }
        public AsyncEnumerable(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new AsyncQueryProvider(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new AsyncEnumerator<T>(this.AsEnumerable().GetEnumerator(), cancellationToken);
    }

    private sealed class AsyncEnumerator<T>(IEnumerator<T> inner, CancellationToken token) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(inner.MoveNext()); }
    }

    private sealed class AsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => (IQueryable)Activator.CreateInstance(typeof(AsyncEnumerable<>).MakeGenericType(expression.Type.GetGenericArguments().Single()), expression)!;
        public IQueryable<T> CreateQuery<T>(Expression expression) => new AsyncEnumerable<T>(expression);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);
        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = inner.Execute(expression);
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(TResult).GetGenericArguments().Single()).Invoke(null, [result])!;
        }
    }
}
