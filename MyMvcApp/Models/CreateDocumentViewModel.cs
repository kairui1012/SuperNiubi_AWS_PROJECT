using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MyMvcApp.Models
{
    public class CreateDocumentViewModel
    {
        [Required(ErrorMessage = "Document name is required")]
        [StringLength(200)]
        public string DocumentName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Document type is required")]
        public DocumentType? DocumentType { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Please choose a file")]
        public IFormFile? File { get; set; }
    }
}
