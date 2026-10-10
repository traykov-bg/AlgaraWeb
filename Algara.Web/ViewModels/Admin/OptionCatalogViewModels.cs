using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Algara.Web.ViewModels.Admin;

public sealed class FabricCatalogViewModel
{
    public string Search { get; set; } = string.Empty;
    public List<Fabric> Fabrics { get; set; } = [];
}

public sealed class ColorCatalogViewModel
{
    public string Search { get; set; } = string.Empty;
    // Null means every collection; zero means standalone colors.
    public int? FabricN { get; set; }
    public List<CatalogColor> Colors { get; set; } = [];
    public List<SelectListItem> Fabrics { get; set; } = [];
}

public sealed class FabricFormViewModel
{
    public int? N { get; set; }

    [StringLength(200, ErrorMessage = "Кодът трябва да е до 200 знака.")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Въведете име на дамаската.")]
    [StringLength(200, ErrorMessage = "Името трябва да е до 200 знака.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Името трябва да е до 200 знака.")]
    public string? EnglishName { get; set; }

    [StringLength(4000, ErrorMessage = "Описанието трябва да е до 4000 знака.")]
    public string? Description { get; set; }

    [StringLength(4000, ErrorMessage = "Описанието трябва да е до 4000 знака.")]
    public string? EnglishDescription { get; set; }

    public IFormFile? ImageFile { get; set; }

    [BindNever, ValidateNever]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class ColorFormViewModel
{
    public int? N { get; set; }
    public int? FabricN { get; set; }

    [Required(ErrorMessage = "Въведете код на цвета.")]
    [StringLength(64, ErrorMessage = "Кодът трябва да е до 64 знака.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Въведете име на цвета.")]
    [StringLength(200, ErrorMessage = "Името трябва да е до 200 знака.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Името трябва да е до 200 знака.")]
    public string? EnglishName { get; set; }

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Изберете цвят от палитрата.")]
    public string? HexColor { get; set; }

    public bool IsActive { get; set; } = true;
    public IFormFile? SwatchFile { get; set; }

    [BindNever, ValidateNever]
    public string? ImageUrl { get; set; }

    [BindNever, ValidateNever]
    public List<SelectListItem> Fabrics { get; set; } = [];

    [BindNever, ValidateNever]
    public string? FabricName { get; set; }
}
