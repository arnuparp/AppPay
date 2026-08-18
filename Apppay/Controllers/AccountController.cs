using Apppay.Data;
using Apppay.Models;
using Apppay.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apppay.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly DefaultCategorySeeder _categorySeeder;
        private readonly ApplicationDbContext _db;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            DefaultCategorySeeder categorySeeder,
            ApplicationDbContext db)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _categorySeeder = categorySeeder;
            _db = db;
        }

        private async Task LogLoginAttemptAsync(string email, string? userId, bool success, string? failureReason)
        {
            _db.LoginLogs.Add(new LoginLog
            {
                Email = email,
                UserId = userId,
                Success = success,
                FailureReason = failureReason,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                LoginAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Transactions");

            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                DisplayName = model.DisplayName
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                await _categorySeeder.SeedForUserAsync(user.Id);
                await _signInManager.SignInAsync(user, isPersistent: false);
                await LogLoginAttemptAsync(model.Email, user.Id, success: true, failureReason: null);
                return RedirectToAction("Index", "Transactions");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Transactions");

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                await LogLoginAttemptAsync(model.Email, user?.Id, success: true, failureReason: null);

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    return Redirect(model.ReturnUrl);

                return RedirectToAction("Index", "Transactions");
            }

            var failureReason = result.IsLockedOut ? "ถูกล็อกบัญชี" : result.IsNotAllowed ? "ไม่ได้รับอนุญาตให้เข้าสู่ระบบ" : "รหัสผ่านไม่ถูกต้อง";
            await LogLoginAttemptAsync(model.Email, user?.Id, success: false, failureReason);

            ModelState.AddModelError(string.Empty, "อีเมลหรือรหัสผ่านไม่ถูกต้อง");
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> LoginHistory()
        {
            var userId = _userManager.GetUserId(User);
            var logs = await _db.LoginLogs
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.LoginAt)
                .Take(100)
                .ToListAsync();

            return View(logs);
        }
    }
}
