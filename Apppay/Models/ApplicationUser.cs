using Microsoft.AspNetCore.Identity;

namespace Apppay.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? DisplayName { get; set; }
    }
}
