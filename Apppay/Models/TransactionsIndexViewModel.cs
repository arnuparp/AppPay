namespace Apppay.Models
{
    public class CategorySummary
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#6c757d";
        public decimal Total { get; set; }
    }

    public class TransactionsIndexViewModel
    {
        public int? Year { get; set; }
        public int? Month { get; set; }

        public List<int> AvailableYears { get; set; } = new();

        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal Balance => TotalIncome - TotalExpense;

        public List<Transaction> Transactions { get; set; } = new();
        public List<CategorySummary> IncomeByCategory { get; set; } = new();
        public List<CategorySummary> ExpenseByCategory { get; set; } = new();
    }
}
