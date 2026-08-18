using Apppay.Data;
using Apppay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Apppay.Controllers
{
    [Authorize]
    public class TransactionsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public TransactionsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index(int? year, int? month)
        {
            var userId = CurrentUserId;

            var baseQuery = _db.Transactions
                .Include(t => t.Category)
                .Where(t => t.UserId == userId);

            var availableYears = await baseQuery
                .Select(t => t.Date.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            var today = DateTime.Today;
            if (!availableYears.Any())
                availableYears.Add(today.Year);
            if (!availableYears.Contains(today.Year))
                availableYears.Insert(0, today.Year);

            var selectedYear = year ?? today.Year;
            var selectedMonth = month ?? (year.HasValue ? month : today.Month);

            var filtered = baseQuery.Where(t => t.Date.Year == selectedYear);
            if (selectedMonth.HasValue && selectedMonth.Value is >= 1 and <= 12)
                filtered = filtered.Where(t => t.Date.Month == selectedMonth.Value);

            var transactions = await filtered
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.Id)
                .ToListAsync();

            var vm = new TransactionsIndexViewModel
            {
                Year = selectedYear,
                Month = selectedMonth,
                AvailableYears = availableYears,
                Transactions = transactions,
                TotalIncome = transactions.Where(t => t.Category!.Type == TransactionType.Income).Sum(t => t.Amount),
                TotalExpense = transactions.Where(t => t.Category!.Type == TransactionType.Expense).Sum(t => t.Amount),
                IncomeByCategory = transactions
                    .Where(t => t.Category!.Type == TransactionType.Income)
                    .GroupBy(t => new { t.Category!.Name, t.Category.Color })
                    .Select(g => new CategorySummary { Name = g.Key.Name, Color = g.Key.Color, Total = g.Sum(t => t.Amount) })
                    .OrderByDescending(c => c.Total)
                    .ToList(),
                ExpenseByCategory = transactions
                    .Where(t => t.Category!.Type == TransactionType.Expense)
                    .GroupBy(t => new { t.Category!.Name, t.Category.Color })
                    .Select(g => new CategorySummary { Name = g.Key.Name, Color = g.Key.Color, Total = g.Sum(t => t.Amount) })
                    .OrderByDescending(c => c.Total)
                    .ToList()
            };

            return View(vm);
        }

        private async Task PopulateCategoriesAsync(TransactionType? type = null)
        {
            var query = _db.Categories.Where(c => c.UserId == CurrentUserId);
            if (type.HasValue)
                query = query.Where(c => c.Type == type.Value);

            var categories = await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();

            ViewBag.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = $"{(c.Type == TransactionType.Income ? "รายรับ" : "รายจ่าย")} - {c.Name}"
            }).ToList();
        }

        public async Task<IActionResult> Create()
        {
            await PopulateCategoriesAsync();
            if (!((List<SelectListItem>)ViewBag.Categories).Any())
            {
                TempData["Error"] = "กรุณาเพิ่มหมวดหมู่ก่อนบันทึกรายการ";
                return RedirectToAction("Create", "Categories");
            }

            return View(new Transaction { Date = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Transaction transaction)
        {
            ModelState.Remove(nameof(Transaction.UserId));
            ModelState.Remove(nameof(Transaction.Category));

            var categoryOwned = await _db.Categories.AnyAsync(c => c.Id == transaction.CategoryId && c.UserId == CurrentUserId);
            if (!categoryOwned)
                ModelState.AddModelError(nameof(Transaction.CategoryId), "หมวดหมู่ไม่ถูกต้อง");

            if (!ModelState.IsValid)
            {
                await PopulateCategoriesAsync();
                return View(transaction);
            }

            transaction.UserId = CurrentUserId;
            transaction.CreatedAt = DateTime.Now;

            _db.Transactions.Add(transaction);
            await _db.SaveChangesAsync();
            TempData["Success"] = "บันทึกรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year = transaction.Date.Year, month = transaction.Date.Month });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (transaction == null) return NotFound();

            await PopulateCategoriesAsync();
            return View(transaction);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Transaction transaction)
        {
            if (id != transaction.Id) return NotFound();

            ModelState.Remove(nameof(Transaction.UserId));
            ModelState.Remove(nameof(Transaction.Category));

            var categoryOwned = await _db.Categories.AnyAsync(c => c.Id == transaction.CategoryId && c.UserId == CurrentUserId);
            if (!categoryOwned)
                ModelState.AddModelError(nameof(Transaction.CategoryId), "หมวดหมู่ไม่ถูกต้อง");

            if (!ModelState.IsValid)
            {
                await PopulateCategoriesAsync();
                return View(transaction);
            }

            var existing = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (existing == null) return NotFound();

            existing.Date = transaction.Date;
            existing.CategoryId = transaction.CategoryId;
            existing.Amount = transaction.Amount;
            existing.Note = transaction.Note;

            await _db.SaveChangesAsync();
            TempData["Success"] = "แก้ไขรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year = existing.Date.Year, month = existing.Date.Month });
        }

        public async Task<IActionResult> Delete(int id)
        {
            var transaction = await _db.Transactions
                .Include(t => t.Category)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (transaction == null) return NotFound();

            return View(transaction);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (transaction == null) return NotFound();

            var year = transaction.Date.Year;
            var month = transaction.Date.Month;

            _db.Transactions.Remove(transaction);
            await _db.SaveChangesAsync();
            TempData["Success"] = "ลบรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year, month });
        }
    }
}
