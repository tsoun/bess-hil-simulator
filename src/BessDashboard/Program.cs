using System.IO;
using BessHilSimulator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace BessDashboard;

static class BessWebServer
{
    public static WebApplication Build(string url)
    {
        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath    = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.UseUrls(url);
        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/default-profile", () =>
        {
            var path = FindFile("input_profile.csv");
            if (path == null) return Results.NotFound(new { error = "input_profile.csv not found." });
            var lines = File.ReadAllLines(path);
            var (steps, err) = RunSimulation(lines);
            if (!string.IsNullOrEmpty(err)) return Results.BadRequest(new { error = err });
            return Results.Ok(steps);
        });

        app.MapPost("/api/simulate", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var csv = await reader.ReadToEndAsync();
            var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var (steps, err) = RunSimulation(lines);
            if (!string.IsNullOrEmpty(err)) return Results.BadRequest(new { error = err });
            return Results.Ok(steps);
        });

        return app;
    }

    static (List<SimStep> steps, string error) RunSimulation(string[] lines)
    {
        if (lines.Length <= 1)
            return ([], "CSV contains no data rows.");

        string[] headers = lines[0].Split(',');
        int timeIdx = 0, pIdx = 1, qIdx = 2, vIdx = 3, fIdx = 4;
        for (int i = 0; i < headers.Length; i++)
        {
            string h = headers[i].Trim().ToLower();
            if (h.StartsWith("time")) timeIdx = i;
            else if (h.Contains("setp") || h.Contains("active")) pIdx = i;
            else if (h.Contains("setq") || h.Contains("reactive")) qIdx = i;
            else if (h.Contains("gridv") || h.Contains("voltage")) vIdx = i;
            else if (h.Contains("gridf") || h.Contains("frequency")) fIdx = i;
        }

        var rows = new List<InputRow>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length <= Math.Max(timeIdx, Math.Max(pIdx, qIdx))) continue;
            try
            {
                var row = new InputRow
                {
                    Time      = double.Parse(parts[timeIdx]),
                    SetpointP = double.Parse(parts[pIdx]),
                    SetpointQ = double.Parse(parts[qIdx]),
                };
                if (vIdx < parts.Length && double.TryParse(parts[vIdx], out double v)) row.GridV = v;
                if (fIdx < parts.Length && double.TryParse(parts[fIdx], out double f)) row.GridF = f;
                rows.Add(row);
            }
            catch { }
        }

        if (rows.Count == 0)
            return ([], "No valid rows found.");

        double Ts = rows.Count > 1 ? Math.Max(rows[1].Time - rows[0].Time, 0.001) : 0.1;
        string pqPath = FindFile("pq-curves.json") ?? "pq-curves.json";
        var plant = new BessPhysicsModel(Ts, 0.2, 0.1, 0.5, 0.21, pqCurvesPath: pqPath);

        var steps = new List<SimStep>(rows.Count);
        foreach (var row in rows)
        {
            var y = plant.Step(row.SetpointP, row.SetpointQ, row.GridV, row.GridF, row.Time);
            steps.Add(new SimStep(row, y));
        }
        return (steps, "");
    }

    static string? FindFile(string name)
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

record SimStep(
    double time, double inputP, double inputQ,
    double setpointP, double setpointQ,
    double physP, double physQ, double physPF, double physV, double physF, double physI,
    double measP, double measQ, double measPF, double measV, double measF, double measI,
    double maxQ, double minQ, double soc, double soh, double tempCelsius)
{
    public SimStep(InputRow r, PlantOutput y) : this(
        r.Time, r.SetpointP, r.SetpointQ,
        y.SetpointP, y.SetpointQ,
        y.PhysP, y.PhysQ, y.PhysPF, y.PhysV, y.PhysF, y.PhysI,
        y.MeasP, y.MeasQ, y.MeasPF, y.MeasV, y.MeasF, y.MeasI,
        y.MaxQ, y.MinQ, y.Soc, y.Soh, y.TempCelsius) { }
}
