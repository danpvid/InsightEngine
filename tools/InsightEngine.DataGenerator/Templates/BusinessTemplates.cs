using InsightEngine.DataGenerator.Models;

namespace InsightEngine.DataGenerator.Templates;

public static class BusinessTemplates
{
    public static DatasetTemplate EcommerceSales()
    {
        return new DatasetTemplate
        {
            Name = "ecommerce_sales",
            Description = "E-commerce sales transactions with various data types",
            RowCount = 5000,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "transaction_id", Type = ColumnType.String, NullRate = 0.0 },
                new() { Name = "sale_date", Type = ColumnType.Date, NullRate = 0.0, 
                       DateRange = (new DateTime(2024, 1, 1), new DateTime(2026, 2, 14)) },
                new() { Name = "amount", Type = ColumnType.Number, NullRate = 0.01, 
                       NumberRange = (10.00m, 5000.00m) },
                new() { Name = "quantity", Type = ColumnType.Number, NullRate = 0.0, 
                       NumberRange = (1, 100) },
                new() { Name = "discount_rate", Type = ColumnType.Number, NullRate = 0.15, 
                       NumberRange = (0.00m, 0.50m) },
                new() { Name = "status", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "completed", "pending", "cancelled", "refunded" } },
                new() { Name = "payment_method", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "credit_card", "debit_card", "pix", "boleto", "paypal" } },
                new() { Name = "is_prime_member", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "is_gift", Type = ColumnType.Boolean, NullRate = 0.05 },
                new() { Name = "customer_segment", Type = ColumnType.Category, NullRate = 0.02, 
                       PossibleValues = new List<string> { "VIP", "Regular", "New", "Inactive" } },
                new() { Name = "product_category", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Electronics", "Clothing", "Home", "Books", "Sports", "Toys" } },
                new() { Name = "shipping_notes", Type = ColumnType.String, NullRate = 0.30, 
                       PossibleValues = new List<string> { "Express delivery", "Standard shipping", "Pickup", "Gift wrap" } }
            }
        };
    }

    public static DatasetTemplate EmployeeRecords()
    {
        return new DatasetTemplate
        {
            Name = "employee_records",
            Description = "HR employee records with various attributes",
            RowCount = 8000,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "employee_id", Type = ColumnType.String, NullRate = 0.0 },
                new() { Name = "hire_date", Type = ColumnType.Date, NullRate = 0.0, 
                       DateRange = (new DateTime(2015, 1, 1), new DateTime(2025, 12, 31)) },
                new() { Name = "birth_date", Type = ColumnType.Date, NullRate = 0.01, 
                       DateRange = (new DateTime(1960, 1, 1), new DateTime(2000, 12, 31)) },
                new() { Name = "salary", Type = ColumnType.Number, NullRate = 0.0, 
                       NumberRange = (3000.00m, 25000.00m) },
                new() { Name = "bonus", Type = ColumnType.Number, NullRate = 0.25, 
                       NumberRange = (0.00m, 10000.00m) },
                new() { Name = "department", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Engineering", "Sales", "Marketing", "HR", "Finance", "Operations" } },
                new() { Name = "position", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Junior", "Mid-Level", "Senior", "Lead", "Manager", "Director" } },
                new() { Name = "is_remote", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "has_benefits", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "performance_rating", Type = ColumnType.Category, NullRate = 0.10, 
                       PossibleValues = new List<string> { "Outstanding", "Excellent", "Good", "Needs Improvement" } },
                new() { Name = "office_location", Type = ColumnType.Category, NullRate = 0.05, 
                       PossibleValues = new List<string> { "São Paulo", "Rio de Janeiro", "Belo Horizonte", "Brasília", "Remote" } },
                new() { Name = "skills", Type = ColumnType.String, NullRate = 0.15, 
                       PossibleValues = new List<string> { "C#", "Python", "Java", "SQL", "React", "AWS", "Leadership" } }
            }
        };
    }

    public static DatasetTemplate FinancialTransactions()
    {
        return new DatasetTemplate
        {
            Name = "financial_transactions",
            Description = "Banking and financial transactions",
            RowCount = 10000,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "transaction_id", Type = ColumnType.String, NullRate = 0.0 },
                new() { Name = "transaction_date", Type = ColumnType.Date, NullRate = 0.0, 
                       DateRange = (new DateTime(2025, 1, 1), new DateTime(2026, 2, 14)) },
                new() { Name = "amount", Type = ColumnType.Number, NullRate = 0.0, 
                       NumberRange = (-50000.00m, 50000.00m) },
                new() { Name = "balance_after", Type = ColumnType.Number, NullRate = 0.01, 
                       NumberRange = (0.00m, 1000000.00m) },
                new() { Name = "fee", Type = ColumnType.Number, NullRate = 0.20, 
                       NumberRange = (0.00m, 50.00m) },
                new() { Name = "transaction_type", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "deposit", "withdrawal", "transfer", "payment", "fee" } },
                new() { Name = "channel", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "ATM", "Mobile App", "Web", "Branch", "POS" } },
                new() { Name = "is_international", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "is_recurring", Type = ColumnType.Boolean, NullRate = 0.05 },
                new() { Name = "status", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "completed", "pending", "failed", "reversed" } },
                new() { Name = "risk_score", Type = ColumnType.Category, NullRate = 0.10, 
                       PossibleValues = new List<string> { "low", "medium", "high" } },
                new() { Name = "description", Type = ColumnType.String, NullRate = 0.40, 
                       PossibleValues = new List<string> { "ATM Withdrawal", "Online Purchase", "Bill Payment", "Salary Deposit" } }
            }
        };
    }

    public static DatasetTemplate HealthcarePatients()
    {
        return new DatasetTemplate
        {
            Name = "healthcare_patients",
            Description = "Healthcare patient records and visits",
            RowCount = 6000,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "patient_id", Type = ColumnType.String, NullRate = 0.0 },
                new() { Name = "admission_date", Type = ColumnType.Date, NullRate = 0.0, 
                       DateRange = (new DateTime(2024, 1, 1), new DateTime(2026, 2, 14)) },
                new() { Name = "discharge_date", Type = ColumnType.Date, NullRate = 0.15, 
                       DateRange = (new DateTime(2024, 1, 1), new DateTime(2026, 2, 14)) },
                new() { Name = "age", Type = ColumnType.Number, NullRate = 0.0, 
                       NumberRange = (0, 100) },
                new() { Name = "total_cost", Type = ColumnType.Number, NullRate = 0.05, 
                       NumberRange = (100.00m, 50000.00m) },
                new() { Name = "length_of_stay", Type = ColumnType.Number, NullRate = 0.10, 
                       NumberRange = (1, 30) },
                new() { Name = "department", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Emergency", "Cardiology", "Orthopedics", "Pediatrics", "Surgery", "ICU" } },
                new() { Name = "insurance_type", Type = ColumnType.Category, NullRate = 0.05, 
                       PossibleValues = new List<string> { "Private", "Public", "Self-Pay", "Medicare" } },
                new() { Name = "is_emergency", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "is_readmission", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "severity", Type = ColumnType.Category, NullRate = 0.02, 
                       PossibleValues = new List<string> { "Critical", "High", "Medium", "Low" } },
                new() { Name = "diagnosis", Type = ColumnType.String, NullRate = 0.08, 
                       PossibleValues = new List<string> { "Fracture", "Infection", "Cardiac Event", "Respiratory Issue" } }
            }
        };
    }

    public static DatasetTemplate LogisticsShipments()
    {
        return new DatasetTemplate
        {
            Name = "logistics_shipments",
            Description = "Logistics and shipping operations",
            RowCount = 7500,
            Columns = new List<ColumnDefinition>
            {
                new() { Name = "shipment_id", Type = ColumnType.String, NullRate = 0.0 },
                new() { Name = "ship_date", Type = ColumnType.Date, NullRate = 0.0, 
                       DateRange = (new DateTime(2025, 1, 1), new DateTime(2026, 2, 14)) },
                new() { Name = "delivery_date", Type = ColumnType.Date, NullRate = 0.20, 
                       DateRange = (new DateTime(2025, 1, 1), new DateTime(2026, 2, 28)) },
                new() { Name = "weight_kg", Type = ColumnType.Number, NullRate = 0.01, 
                       NumberRange = (0.5m, 1000.0m) },
                new() { Name = "shipping_cost", Type = ColumnType.Number, NullRate = 0.0, 
                       NumberRange = (5.00m, 500.00m) },
                new() { Name = "distance_km", Type = ColumnType.Number, NullRate = 0.05, 
                       NumberRange = (1.0m, 5000.0m) },
                new() { Name = "carrier", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Correios", "Fedex", "DHL", "Local Courier", "Own Fleet" } },
                new() { Name = "service_level", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "Express", "Standard", "Economy", "Same-Day" } },
                new() { Name = "is_insured", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "requires_signature", Type = ColumnType.Boolean, NullRate = 0.0 },
                new() { Name = "status", Type = ColumnType.Category, NullRate = 0.0, 
                       PossibleValues = new List<string> { "delivered", "in_transit", "delayed", "returned", "lost" } },
                new() { Name = "destination_region", Type = ColumnType.Category, NullRate = 0.02, 
                       PossibleValues = new List<string> { "Southeast", "South", "Northeast", "North", "Midwest", "International" } },
                new() { Name = "special_instructions", Type = ColumnType.String, NullRate = 0.60, 
                       PossibleValues = new List<string> { "Fragile", "Keep Refrigerated", "Handle with Care", "Leave at Door" } }
            }
        };
    }

    public static List<DatasetTemplate> GetAllTemplates()
    {
        return new List<DatasetTemplate>
        {
            EcommerceSales(),
            EmployeeRecords(),
            FinancialTransactions(),
            HealthcarePatients(),
            LogisticsShipments()
        };
    }
}
