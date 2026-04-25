namespace MyMvcApp.Models
{
	public class MaintenanceRequestViewModel
	{
		public List<MaintenanceRequest> Requests { get; set; } = new();

        public CreateMaintenanceViewModel NewRequest { get; set; } = new();
    }
}
