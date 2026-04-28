using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MyMvcApp.Models
{
    public class LandlordDocumentsViewModel
    {
        public List<Document> Documents { get; set; } = new();
        public CreateLandlordDocumentViewModel NewDocument { get; set; } = new();
        public List<SelectListItem> PropertyOptions { get; set; } = new();
        public List<SelectListItem> TenantOptions { get; set; } = new();
    }

    public class CreateLandlordDocumentViewModel
    {
        [Required(ErrorMessage = "Document name is required")]
        [StringLength(200)]
        public string DocumentName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Document type is required")]
        public DocumentType? DocumentType { get; set; }

        public int? PropertyId { get; set; }

        public int? TenantId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Please choose a file")]
        public IFormFile? File { get; set; }
    }
}
