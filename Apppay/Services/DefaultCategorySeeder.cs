using Apppay.Data;
using Apppay.Models;

namespace Apppay.Services
{
    public class DefaultCategorySeeder
    {
        private readonly ApplicationDbContext _db;

        public DefaultCategorySeeder(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task SeedForUserAsync(string userId)
        {
            var defaults = new List<Category>
            {
                new() { Name = "เงินเดือน", Type = TransactionType.Income, Color = "#198754", UserId = userId },
                new() { Name = "รายได้พิเศษ", Type = TransactionType.Income, Color = "#20c997", UserId = userId },
                new() { Name = "อาหาร", Type = TransactionType.Expense, Color = "#dc3545", UserId = userId },
                new() { Name = "เดินทาง", Type = TransactionType.Expense, Color = "#fd7e14", UserId = userId },
                new() { Name = "ที่พัก/ค่าเช่า", Type = TransactionType.Expense, Color = "#6f42c1", UserId = userId },
                new() { Name = "ช้อปปิ้ง", Type = TransactionType.Expense, Color = "#d63384", UserId = userId },
                new() { Name = "สาธารณูปโภค", Type = TransactionType.Expense, Color = "#0dcaf0", UserId = userId },
                new() { Name = "อื่นๆ", Type = TransactionType.Expense, Color = "#6c757d", UserId = userId },
            };

            _db.Categories.AddRange(defaults);
            await _db.SaveChangesAsync();
        }
    }
}
