using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Apppay.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "กรุณาระบุชื่อหมวดหมู่")]
        [StringLength(100)]
        [Display(Name = "ชื่อหมวดหมู่")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "ประเภท")]
        public TransactionType Type { get; set; }

        [StringLength(20)]
        [Display(Name = "สี")]
        public string Color { get; set; } = "#6c757d";

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        [NotMapped]
        public bool HasTransactions { get; set; }
    }
}
