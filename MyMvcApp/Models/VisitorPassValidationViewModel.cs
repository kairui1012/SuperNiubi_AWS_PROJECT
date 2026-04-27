namespace MyMvcApp.Models
{
    public class VisitorPassValidationViewModel
    {
        public string PassCode { get; set; } = string.Empty;
        public bool Found { get; set; }
        public bool IsValid { get; set; }
        public bool CheckedIn { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public VisitorPass? Pass { get; set; }
    }
}
