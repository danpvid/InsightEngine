using InsightEngine.DataGenerator.Generators;
using InsightEngine.DataGenerator.Templates;

namespace InsightEngine.DataGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  InsightEngine - Semantic CSV Data Generator");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Create output directory (relative to solution root)
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = Path.GetFullPath(Path.Combine(currentDir, "..", ".."));
        var samplesPath = Path.Combine(projectRoot, "samples");
        Directory.CreateDirectory(samplesPath);

        Console.WriteLine($"📁 Output directory: {samplesPath}");
        Console.WriteLine();

        var generator = new CsvGenerator();
        var templates = BusinessTemplates.GetAllTemplates();

        Console.WriteLine($"📊 Generating {templates.Count} datasets...");
        Console.WriteLine();

        foreach (var template in templates)
        {
            var outputFile = Path.Combine(samplesPath, $"{template.Name}.csv");
            
            try
            {
                await generator.GenerateAsync(template, outputFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error generating {template.Name}: {ex.Message}");
                Console.WriteLine();
            }
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("✅ All datasets generated successfully!");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("You can now test the InsightEngine API with these samples:");
        Console.WriteLine();

        foreach (var template in templates)
        {
            Console.WriteLine($"  • {template.Name}.csv - {template.Description}");
            Console.WriteLine($"    Rows: {template.RowCount:N0}, Columns: {template.Columns.Count}");
            Console.WriteLine();
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
