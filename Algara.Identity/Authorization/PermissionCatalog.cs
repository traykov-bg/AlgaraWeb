namespace Algara.Identity.Authorization;

public sealed record PermissionDefinition(string Key, string Module, string Name, bool AdminOnly = false);

public static class StaffRoles
{
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";
    public const string CatalogEditor = "CatalogEditor";
    public const string OrderManager = "OrderManager";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Admin, Moderator, CatalogEditor, OrderManager });
    public static bool IsStaff(string name) => All.Contains(name, StringComparer.Ordinal);
}

public static class PermissionCatalog
{
    public static class Dashboard { public const string View = "Dashboard.View"; }
    public static class Products
    {
        public const string View = "Products.View", Create = "Products.Create", Edit = "Products.Edit",
            ChangePrice = "Products.ChangePrice", ChangeStock = "Products.ChangeStock", Deactivate = "Products.Deactivate";
    }
    public static class Options { public const string View = "Options.View", Create = "Options.Create", Edit = "Options.Edit", Deactivate = "Options.Deactivate"; }
    public static class Categories { public const string View = "Categories.View", Create = "Categories.Create", Edit = "Categories.Edit", Deactivate = "Categories.Deactivate", Delete = "Categories.Delete"; }
    public static class Content { public const string View = "Content.View", Create = "Content.Create", Edit = "Content.Edit", Deactivate = "Content.Deactivate"; }
    public static class Promotions { public const string View = "Promotions.View", Create = "Promotions.Create", Edit = "Promotions.Edit", Deactivate = "Promotions.Deactivate", Delete = "Promotions.Delete"; }
    public static class Orders { public const string View = "Orders.View", ChangeStatus = "Orders.ChangeStatus"; }
    public static class Companies { public const string View = "Companies.View", Create = "Companies.Create", Edit = "Companies.Edit", Deactivate = "Companies.Deactivate"; }
    public static class Partners { public const string View = "Partners.View", Create = "Partners.Create", Edit = "Partners.Edit", Deactivate = "Partners.Deactivate", Move = "Partners.Move"; }
    public static class PartnerGroups { public const string View = "PartnerGroups.View", Create = "PartnerGroups.Create", Edit = "PartnerGroups.Edit", Move = "PartnerGroups.Move"; }
    public static class Users { public const string View = "Users.View"; }
    public static class Access { public const string AssignRoles = "Access.AssignRoles", ManagePermissions = "Access.ManagePermissions", ViewHistory = "Access.ViewHistory"; }
    public static class Settings { public const string Manage = "Settings.Manage"; }

    public static IReadOnlyList<PermissionDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new PermissionDefinition(Dashboard.View,"Табло","Преглед"),
        new(Products.View,"Продукти","Преглед"), new(Products.Create,"Продукти","Създаване"), new(Products.Edit,"Продукти","Редакция"),
        new(Products.ChangePrice,"Продукти","Промяна на цени"), new(Products.ChangeStock,"Продукти","Промяна на наличности"), new(Products.Deactivate,"Продукти","Активиране и деактивиране"),
        new(Options.View,"Дамаски и цветове","Преглед"), new(Options.Create,"Дамаски и цветове","Създаване"), new(Options.Edit,"Дамаски и цветове","Редакция"), new(Options.Deactivate,"Дамаски и цветове","Активиране и деактивиране"),
        new(Categories.View,"Категории","Преглед"), new(Categories.Create,"Категории","Създаване"), new(Categories.Edit,"Категории","Редакция"), new(Categories.Deactivate,"Категории","Активиране и деактивиране"), new(Categories.Delete,"Категории","Изтриване"),
        new(Content.View,"Съдържание","Преглед"), new(Content.Create,"Съдържание","Създаване"), new(Content.Edit,"Съдържание","Редакция"), new(Content.Deactivate,"Съдържание","Активиране и деактивиране"),
        new(Promotions.View,"Промоции","Преглед"), new(Promotions.Create,"Промоции","Създаване"), new(Promotions.Edit,"Промоции","Редакция"), new(Promotions.Deactivate,"Промоции","Активиране и деактивиране"), new(Promotions.Delete,"Промоции","Изтриване"),
        new(Orders.View,"Поръчки","Преглед"), new(Orders.ChangeStatus,"Поръчки","Промяна на статус"),
        new(Companies.View,"Фирми","Преглед"), new(Companies.Create,"Фирми","Създаване"), new(Companies.Edit,"Фирми","Редакция"), new(Companies.Deactivate,"Фирми","Активиране и деактивиране"),
        new(Partners.View,"Контрагенти","Преглед"), new(Partners.Create,"Контрагенти","Създаване"), new(Partners.Edit,"Контрагенти","Редакция"), new(Partners.Deactivate,"Контрагенти","Активиране и деактивиране"), new(Partners.Move,"Контрагенти","Преместване"),
        new(PartnerGroups.View,"Групи контрагенти","Преглед"), new(PartnerGroups.Create,"Групи контрагенти","Създаване"), new(PartnerGroups.Edit,"Групи контрагенти","Редакция"), new(PartnerGroups.Move,"Групи контрагенти","Преместване"),
        new(Users.View,"Потребители","Преглед"), new(Access.AssignRoles,"Достъп","Даване и отнемане на роли",true),
        new(Access.ManagePermissions,"Достъп","Матрица на правата",true), new(Access.ViewHistory,"Достъп","Преглед на историята"),
        new(Settings.Manage,"Настройки","Критични настройки",true)
    });

    public static PermissionDefinition? Find(string key) => All.FirstOrDefault(p => p.Key == key);

    public static IReadOnlyList<string> DefaultGrants(string role) => All.Where(p => role switch
    {
        StaffRoles.Admin => true,
        StaffRoles.Moderator => !p.AdminOnly,
        StaffRoles.CatalogEditor => new[] { "Dashboard", "Products", "Options", "Categories", "Content", "Promotions" }.Contains(p.Key.Split('.')[0]),
        StaffRoles.OrderManager => new[] { "Dashboard", "Orders", "Companies", "Partners", "PartnerGroups", "Users" }.Contains(p.Key.Split('.')[0]),
        _ => false
    }).Select(p => p.Key).ToArray();
}
