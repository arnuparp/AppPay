namespace Apppay.Models
{
    public class LoginLog
    {
        public int Id { get; set; }

        public string? UserId { get; set; }

        public ApplicationUser? User { get; set; }

        public string Email { get; set; } = string.Empty;

        public bool Success { get; set; }

        public string? FailureReason { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public DateTime LoginAt { get; set; } = DateTime.Now;
    }
}
