using Algara.Data.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminProductFormViewModel
    {
        public int N { get; set; }

        [MaxLength(64)]
        [Display(Name = "Артикулен код")]
        public string? Sku { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(300)]
        [Display(Name = "Наименование")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [MaxLength(4000)]
        [Display(Name = "Материали")]
        public string? Materials { get; set; }

        [MaxLength(300)]
        [Display(Name = "Наименование на английски")]
        public string? EnglishName { get; set; }

        [Display(Name = "Описание на английски")]
        public string? EnglishDescription { get; set; }

        [MaxLength(4000)]
        [Display(Name = "Материали на английски")]
        public string? EnglishMaterials { get; set; }

        [Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Въведете размер между 0,01 и 100 000 см.")]
        [Display(Name = "Ширина (см)")]
        public decimal? WidthCm { get; set; }

        [Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Въведете размер между 0,01 и 100 000 см.")]
        [Display(Name = "Височина (см)")]
        public decimal? HeightCm { get; set; }

        [Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Въведете размер между 0,01 и 100 000 см.")]
        [Display(Name = "Дълбочина (см)")]
        public decimal? DepthCm { get; set; }

        [Required(ErrorMessage = "Цената е задължителна")]
        [Range(0.01, 9_999_999, ErrorMessage = "Невалидна цена")]
        [Display(Name = "Цена (€)")]
        public decimal Price { get; set; }

        public string? RowVersion { get; set; }
        public ProductOptionKind OptionKind { get; set; }
        [Range(0, 1_000_000)]
        public int? StockQuantity { get; set; }
        public List<AdminProductVariantViewModel> Variants { get; set; } = [];
        public List<AdminProductFabricSurchargeViewModel> FabricSurcharges { get; set; } = [];
        [BindNever]
        public List<CatalogColor> AvailableColors { get; set; } = [];
        [BindNever]
        public List<Fabric> AvailableFabrics { get; set; } = [];

        public string? GalleryDraftId { get; set; }
        public List<AdminProductImageViewModel> Images { get; set; } = [];

        [Display(Name = "По поръчка")]
        public bool IsCustomizable { get; set; }

        [Display(Name = "Препоръчан")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Активен")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Категория")]
        public int? CategoryN { get; set; }

        // Попълва се от контролера за dropdown-а
        public IEnumerable<Category> Categories { get; set; } = [];

        // Под-категории
        public List<int> SelectedSubCategoryNs { get; set; } = new();
        public List<SubCategory> AvailableSubCategories { get; set; } = new();
    }

    public class AdminProductImageViewModel
    {
        public int N { get; set; }

        [Range(0, 10000)]
        public int DisplayOrder { get; set; }
        public string? UploadId { get; set; }

        [BindNever]
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class AdminProductVariantViewModel
    {
        public int N { get; set; }
        public int CatalogColorN { get; set; }
        [MaxLength(64)]
        public string? Sku { get; set; }
        [Range(0, 1_000_000)]
        public int? StockQuantity { get; set; }
        public decimal Surcharge { get; set; }
        public int? ProductImageN { get; set; }
        public string? ProductImageUploadId { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class AdminProductFabricSurchargeViewModel
    {
        public int FabricN { get; set; }
        public decimal Surcharge { get; set; }
    }
}
