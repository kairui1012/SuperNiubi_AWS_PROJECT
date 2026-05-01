using Amazon.XRay.Recorder.Core;
using System.Security.Claims;

namespace MyMvcApp.Middlewares
{
    public class XRayUserTrackingMiddleware
    {
        private readonly RequestDelegate _next;

        public XRayUserTrackingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if the user is logged in
            if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
            {
                // Grab the User ID (NameIdentifier is the standard claim for IDs)
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                
                // Grab their role (e.g., Tenant, Landlord, Admin)
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    // Add searchable annotations!
                    AWSXRayRecorder.Instance.AddAnnotation("UserId", userId);
                }
                
                if (!string.IsNullOrEmpty(role))
                {
                    AWSXRayRecorder.Instance.AddAnnotation("UserRole", role);
                }
            }

            // Continue to the actual MVC Controller
            await _next(context);
        }
    }
}