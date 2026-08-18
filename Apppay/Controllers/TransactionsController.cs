using Apppay.Data;
using Apppay.Models;
using Apppay.Services;
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
        private const long MaxSlipFileSize = 10 * 1024 * 1024; // 10 MB ต่อรูป
        private static readonly string[] AllowedSlipExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly SlipOcrService _ocr;

        public TransactionsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, SlipOcrService ocr)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
            _ocr = ocr;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        // เก็บนอก wwwroot เพราะสลิปเป็นข้อมูลการเงินที่ sensitive — ต้องเสิร์ฟผ่าน action ที่เช็คสิทธิ์เท่านั้น ห้ามให้ static file server เข้าถึงตรงๆ
        private string SlipsRootPath => Path.Combine(_env.ContentRootPath, "App_Data", "slips");

        public async Task<IActionResult> Index(int? year, int? month)
        {
            var userId = CurrentUserId;

            var baseQuery = _db.Transactions
                .Include(t => t.Category)
                .Include(t => t.Slips)
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
        public async Task<IActionResult> Create(Transaction transaction, List<IFormFile>? slipFiles)
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

            await SaveSlipFilesAsync(transaction.Id, slipFiles);

            TempData["Success"] = "บันทึกรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year = transaction.Date.Year, month = transaction.Date.Month });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var transaction = await _db.Transactions
                .Include(t => t.Slips)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (transaction == null) return NotFound();

            await PopulateCategoriesAsync();
            return View(transaction);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Transaction transaction, List<IFormFile>? slipFiles)
        {
            if (id != transaction.Id) return NotFound();

            ModelState.Remove(nameof(Transaction.UserId));
            ModelState.Remove(nameof(Transaction.Category));

            var categoryOwned = await _db.Categories.AnyAsync(c => c.Id == transaction.CategoryId && c.UserId == CurrentUserId);
            if (!categoryOwned)
                ModelState.AddModelError(nameof(Transaction.CategoryId), "หมวดหมู่ไม่ถูกต้อง");

            if (!ModelState.IsValid)
            {
                var existingForView = await _db.Transactions.Include(t => t.Slips).FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
                transaction.Slips = existingForView?.Slips ?? new List<TransactionSlip>();
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

            await SaveSlipFilesAsync(existing.Id, slipFiles);

            TempData["Success"] = "แก้ไขรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year = existing.Date.Year, month = existing.Date.Month });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSlip(int id, int transactionId)
        {
            var slip = await _db.TransactionSlips
                .Include(s => s.Transaction)
                .FirstOrDefaultAsync(s => s.Id == id && s.Transaction!.UserId == CurrentUserId);
            if (slip == null) return NotFound();

            var filePath = Path.Combine(SlipsRootPath, CurrentUserId, slip.TransactionId.ToString(), slip.FileName);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            _db.TransactionSlips.Remove(slip);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Edit), new { id = transactionId });
        }

        public async Task<IActionResult> ViewSlip(int id)
        {
            var slip = await _db.TransactionSlips
                .Include(s => s.Transaction)
                .FirstOrDefaultAsync(s => s.Id == id && s.Transaction!.UserId == CurrentUserId);
            if (slip == null) return NotFound();

            var filePath = Path.Combine(SlipsRootPath, CurrentUserId, slip.TransactionId.ToString(), slip.FileName);
            if (!System.IO.File.Exists(filePath)) return NotFound();

            var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            return PhysicalFile(filePath, contentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScanSlips(List<IFormFile>? files)
        {
            var amounts = new List<decimal?>();
            if (files == null || files.Count == 0)
                return Json(new { amounts, total = 0m });

            var imageBytesList = new List<byte[]?>();
            foreach (var file in files)
            {
                if (file.Length == 0 || file.Length > MaxSlipFileSize)
                {
                    imageBytesList.Add(null);
                    continue;
                }

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!AllowedSlipExtensions.Contains(ext))
                {
                    imageBytesList.Add(null);
                    continue;
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                imageBytesList.Add(ms.ToArray());
            }

            var validBytes = imageBytesList.Where(b => b != null).Select(b => b!).ToList();
            var readAmounts = _ocr.TryReadAmounts(validBytes);

            var readIndex = 0;
            foreach (var bytes in imageBytesList)
            {
                if (bytes == null)
                {
                    amounts.Add(null);
                }
                else
                {
                    amounts.Add(readAmounts[readIndex]);
                    readIndex++;
                }
            }

            var total = amounts.Where(a => a.HasValue).Sum(a => a!.Value);
            return Json(new { amounts, total });
        }

        private async Task SaveSlipFilesAsync(int transactionId, List<IFormFile>? files)
        {
            if (files == null || files.Count == 0) return;

            var folder = Path.Combine(SlipsRootPath, CurrentUserId, transactionId.ToString());
            Directory.CreateDirectory(folder);

            foreach (var file in files)
            {
                if (file.Length == 0 || file.Length > MaxSlipFileSize) continue;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!AllowedSlipExtensions.Contains(ext)) continue;

                var storedName = $"{Guid.NewGuid()}{ext}";
                var fullPath = Path.Combine(folder, storedName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _db.TransactionSlips.Add(new TransactionSlip
                {
                    TransactionId = transactionId,
                    FileName = storedName,
                    OriginalFileName = Path.GetFileName(file.FileName)
                });
            }

            await _db.SaveChangesAsync();
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

            var folder = Path.Combine(SlipsRootPath, CurrentUserId, id.ToString());
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);

            TempData["Success"] = "ลบรายการเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index), new { year, month });
        }
    }
}
