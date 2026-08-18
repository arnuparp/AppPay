using System.ComponentModel.DataAnnotations;

namespace Apppay.Models
{
    public class TransactionSlip
    {
        public int Id { get; set; }

        [Required]
        public int TransactionId { get; set; }

        public Transaction? Transaction { get; set; }

        [Required]
        [StringLength(260)]
        public string FileName { get; set; } = string.Empty;

        [StringLength(260)]
        public string OriginalFileName { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}
