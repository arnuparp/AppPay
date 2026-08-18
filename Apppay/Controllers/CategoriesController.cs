using Apppay.Data;
using Apppay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apppay.Controllers
{
    [Authorize]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public CategoriesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index()
        {
            var categories = await _db.Categories
                .Where(c => c.UserId == CurrentUserId)
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return View(categories);
        }

        public IActionResult Create()
        {
            return View(new Category());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Category category)
        {
            ModelState.Remove(nameof(Category.UserId));

            if (!ModelState.IsValid)
                return View(category);

            category.UserId = CurrentUserId;

            var exists = await _db.Categories.AnyAsync(c =>
                c.UserId == CurrentUserId && c.Name == category.Name && c.Type == category.Type);
            if (exists)
            {
                ModelState.AddModelError(nameof(Category.Name), "มีหมวดหมู่นี้อยู่แล้ว");
                return View(category);
            }

            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
            TempData["Success"] = "เพิ่มหมวดหมู่เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);
            if (category == null) return NotFound();
            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Category category)
        {
            if (id != category.Id) return NotFound();

            ModelState.Remove(nameof(Category.UserId));
            if (!ModelState.IsValid)
                return View(category);

            var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);
            if (existing == null) return NotFound();

            var duplicate = await _db.Categories.AnyAsync(c =>
                c.UserId == CurrentUserId && c.Id != id && c.Name == category.Name && c.Type == category.Type);
            if (duplicate)
            {
                ModelState.AddModelError(nameof(Category.Name), "มีหมวดหมู่นี้อยู่แล้ว");
                return View(category);
            }

            existing.Name = category.Name;
            existing.Type = category.Type;
            existing.Color = category.Color;

            await _db.SaveChangesAsync();
            TempData["Success"] = "แก้ไขหมวดหมู่เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int id)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);
            if (category == null) return NotFound();

            category.HasTransactions = await _db.Transactions.AnyAsync(t => t.CategoryId == id);
            return View(category);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);
            if (category == null) return NotFound();

            var inUse = await _db.Transactions.AnyAsync(t => t.CategoryId == id);
            if (inUse)
            {
                TempData["Error"] = "ไม่สามารถลบหมวดหมู่นี้ได้ เนื่องจากมีรายการที่ใช้งานอยู่";
                return RedirectToAction(nameof(Index));
            }

            _db.Categories.Remove(category);
            await _db.SaveChangesAsync();
            TempData["Success"] = "ลบหมวดหมู่เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Index));
        }
    }
}
