using Algara.Data.Data;
using Algara.Data.Models;

namespace Algara.Web.Data
{
    public static class ShopDbSeeder
    {
        // Bulgarian quotation marks
        private const string Lq = "\u201E"; // „
        private const string Rq = "\u201C"; // "

        private static string Q(string text) => $"{Lq}{text}{Rq}";

        public static async Task SeedAsync(ShopDbContext context)
        {
            if (context.Categories.Any()) return; // вече има данни

            var categories = new List<Category>
            {
                new() { Name = "Мека мебел",  Description = "Дивани, канапета и кресла за всекидневната", IsFeatured = true  },
                new() { Name = "Спалня",      Description = "Легла, матраци и спални комплекти",          IsFeatured = true  },
                new() { Name = "Шкафове",     Description = "Гардероби, витрини и стелажи",               IsFeatured = true  },
                new() { Name = "Маси",        Description = "Трапезни, кафе и работни маси",              IsFeatured = true  },
                new() { Name = "Столове",     Description = "Трапезни, работни и декоративни столове",    IsFeatured = true  },
                new() { Name = "Аксесоари",   Description = "Осветление, декорация и текстил",            IsFeatured = false },
            };
            context.Categories.AddRange(categories);
            await context.SaveChangesAsync();

            // Placeholder изображения — топло кафяво (#D4C5B0 фон, #5C4A3A текст)
            static string Img(string label) =>
                $"https://placehold.co/600x400/D4C5B0/5C4A3A?text={Uri.EscapeDataString(label)}";

            var products = new List<Product>
            {
                // Мека мебел [0]
                new() { Name = $"Диван {Q("Модерна")}",      Description = "Просторен триместен диван с мека тапицерия. Идеален за съвременния дом.", Price = 2850, ImageUrl = Img("Диван Модерна"),   IsFeatured = true,  IsCustomizable = true,  IsActive = true, CategoryN = categories[0].N },
                new() { Name = $"Ъглов диван {Q("Комфорт")}", Description = "Г-образен диван с оттоманка. Максимален комфорт за целия дом.",           Price = 3450, ImageUrl = Img("Ъглов диван"),    IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[0].N },
                new() { Name = $"Кресло {Q("Релакс")}",      Description = "Уютно кресло с дървени крака и меки облегалки.",                          Price = 890,  ImageUrl = Img("Кресло Релакс"),  IsFeatured = true,  IsCustomizable = false, IsActive = true, CategoryN = categories[0].N },

                // Спалня [1]
                new() { Name = $"Спален комплект {Q("Луна")}", Description = "Легло + 2 нощни шкафчета. Скандинавски дизайн, масивна дървесина.", Price = 1950, ImageUrl = Img("Спален комплект"), IsFeatured = true,  IsCustomizable = true,  IsActive = true, CategoryN = categories[1].N },
                new() { Name = $"Нощно шкафче {Q("Слим")}",   Description = "Минималистично нощно шкафче с едно чекмедже.",                      Price = 350,  ImageUrl = Img("Нощно шкафче"),   IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[1].N },

                // Шкафове [2]
                new() { Name = $"Витрина {Q("Модерн")}",  Description = "Висока витрина с LED осветление и стъклени врати.", Price = 1250, ImageUrl = Img("Витрина"),    IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[2].N },
                new() { Name = $"Стелаж {Q("Куб")}",      Description = "Модулен стелаж \u2014 наредете го по вашему.",     Price = 480,  ImageUrl = Img("Стелаж Куб"), IsFeatured = false, IsCustomizable = true,  IsActive = true, CategoryN = categories[2].N },

                // Маси [3]
                new() { Name = $"Кафе маса {Q("Коте")}",        Description = "Кафе маса от масивен дъб с метални крака.",       Price = 650,  ImageUrl = Img("Кафе маса"),     IsFeatured = true,  IsCustomizable = false, IsActive = true, CategoryN = categories[3].N },
                new() { Name = $"Трапезна маса {Q("Фамилия")}", Description = "Разтегателна маса за 6-10 човека. Масивен бук.", Price = 1890, ImageUrl = Img("Трапезна маса"), IsFeatured = false, IsCustomizable = true,  IsActive = true, CategoryN = categories[3].N },

                // Столове [4]
                new() { Name = $"Трапезен стол {Q("Класик")}", Description = "Масивен дъб с тапицирана седалка. Комплект от 2 броя.", Price = 440, ImageUrl = Img("Трапезен стол"), IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[4].N },
                new() { Name = $"Работен стол {Q("Ерго")}",    Description = "Ергономичен стол с регулируема височина.",              Price = 620, ImageUrl = Img("Работен стол"),   IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[4].N },

                // Аксесоари [5]
                new() { Name = $"Висулкова лампа {Q("Арко")}", Description = "Минималистична висулкова лампа с текстилен кабел.", Price = 320, ImageUrl = Img("Лампа Арко"),  IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[5].N },
                new() { Name = $"Подова лампа {Q("Флора")}",   Description = "Подова лампа с мраморна основа и златист детайл.", Price = 280, ImageUrl = Img("Лампа Флора"), IsFeatured = false, IsCustomizable = false, IsActive = true, CategoryN = categories[5].N },
            };
            context.Products.AddRange(products);
            await context.SaveChangesAsync();
        }
    }
}
