namespace MyMvcApp.Models;

public class GoogleLoginFormViewModel
{
    public required string Mode { get; init; }
    public required string ButtonText { get; init; }
    public required string DataAuthForm { get; init; }
    public bool IsVisible { get; init; }
}
