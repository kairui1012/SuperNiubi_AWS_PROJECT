namespace MyMvcApp.Models
{
    public class AppUser
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public bool IsApproved { get; set; }
        public string Role { get; set; } = "Tenant"; // Default role is Tenant
        public bool IsDisabled { get; set; } = false;
    }
}