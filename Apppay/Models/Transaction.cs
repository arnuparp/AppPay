using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Apppay.Models
{
    public class Transaction
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "วันที่")]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "กรุณาเลือกหมวดหมู่")]
        [Display(Name = "หมวดหมู่")]
        public int CategoryId { get; set; }

        public Category? Category { get; set; }

        [Required(ErrorMessage = "กรุณาระบุจำนวนเงิน")]
        [Range(0.01, 999999999, ErrorMessage = "จำนวนเงินต้องมากกว่า 0")]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "จำนวนเงิน")]
        public decimal Amount { get; set; }

        [StringLength(300)]
        [Display(Name = "รายละเอียด")]
        public string? Note { get; set; }

        [Display(Name = "สร้างเมื่อ")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public ICollection<TransactionSlip> Slips { get; set; } = new List<TransactionSlip>();

        [NotMapped]
        public TransactionType Type => Category?.Type ?? TransactionType.Expense;
    }
}
