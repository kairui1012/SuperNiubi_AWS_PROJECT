namespace MyMvcApp.Models
{
    public class TenantDocumentsViewModel
    {
        public List<Document> Documents { get; set; } = new();
        public CreateDocumentViewModel NewDocument { get; set; } = new();
    }
}
