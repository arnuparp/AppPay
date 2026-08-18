using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;

namespace Apppay.Services
{
    public class SlipOcrService
    {
        private static readonly string TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        private static readonly Regex AmountPattern = new(@"\d{1,3}(?:,\d{3})*\.\d{2}|\d+\.\d{2}", RegexOptions.Compiled);

        // ใช้ TesseractEngine ตัวเดียวประมวลผลหลายรูปในคำขอเดียว (การสร้าง engine ใหม่ทุกรูปช้ากว่ามาก)
        public List<decimal?> TryReadAmounts(IReadOnlyList<byte[]> images)
        {
            var results = new List<decimal?>(images.Count);
            if (images.Count == 0) return results;

            TesseractEngine? engine = null;
            try
            {
                engine = new TesseractEngine(TessDataPath, "eng", EngineMode.Default);
            }
            catch
            {
                // เปิด engine ไม่สำเร็จ (เช่น ไม่พบ native library) — ให้ผู้ใช้กรอกจำนวนเงินเอง
                for (var i = 0; i < images.Count; i++) results.Add(null);
                return results;
            }

            using (engine)
            {
                foreach (var bytes in images)
                {
                    try
                    {
                        using var img = Pix.LoadFromMemory(bytes);
                        using var page = engine.Process(img);
                        results.Add(ExtractLikelyAmount(page.GetText()));
                    }
                    catch
                    {
                        results.Add(null);
                    }
                }
            }

            return results;
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
