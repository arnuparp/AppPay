using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;

namespace Apppay.Services
{
    public class SlipOcrService
    {
        private static readonly string TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        private static readonly Regex AmountPattern = new(@"\d{1,3}(?:,\d{3})*\.\d{2}|\d+\.\d{2}", RegexOptions.Compiled);

        public decimal? TryReadAmount(byte[] imageBytes)
        {
            try
            {
                using var engine = new TesseractEngine(TessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                var text = page.GetText();
                return ExtractLikelyAmount(text);
            }
            catch
            {
                // OCR ล้มเหลว (เช่น รูปไม่ใช่ภาพที่รองรับ) — ให้ผู้ใช้กรอกจำนวนเงินเอง
                return null;
            }
        }

        private static decimal? ExtractLikelyAmount(string text)
        {
            decimal? best = null;
            foreach (Match m in AmountPattern.Matches(text))
            {
                var cleaned = m.Value.Replace(",", "");
                if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                    && value > 0 && value < 100_000_000)
                {
                    if (best == null || value > best)
                        best = value;
                }
            }
            return best;
        }
    }
}
