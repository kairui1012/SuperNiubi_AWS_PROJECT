namespace MyMvcApp.Models
{
	public class MaintenanceRequestViewModel
	{
		public List<MaintenanceRequest> Requests = new();

        public CreateMaintenanceViewModel NewRequest { get; set; } = new();
    }
}
