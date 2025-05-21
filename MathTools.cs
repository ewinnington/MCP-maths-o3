using System;
using System.ComponentModel;                 // For [Description]
using System.Linq;
using System.Text.RegularExpressions;
using AngouriMath;                           // Formula solver
using Google.OrTools.LinearSolver;           // LP solver
using ModelContextProtocol.Server;
using static AngouriMath.Entity.Set;           // MCP attributes

[McpServerToolType]
public static class MathTools
{
    // -------------------- solveFormulaForX --------------------
    [McpServerTool,
     Description("Solve an equation for x (first real root). " +
                 "Example: \"x^2 + 7 = 43\" → 6")]
    public static double SolveFormulaForX(
        [Description("Equation involving x, e.g. \"x^2 + 7 = 43\"")]
        string formula = "x^2 + 7 = 43")
    {
        // log invocation
        Console.Error.WriteLine($"[SolveFormulaForX] Invoked with formula: '{formula}'");
        try
        {

            // 2) solve symbolically → returns an Entity.Set
            var solutions = MathS.FromString(formula).Solve("x");
            Console.Error.WriteLine($"[SolveFormulaForX] Raw solutions object: {solutions}");

            // 3) cast to a FiniteSet so we can enumerate
            if (solutions is not FiniteSet fs)
                throw new InvalidOperationException("No finite solutions found.");

            Console.Error.WriteLine($"[SolveFormulaForX] FiniteSet elements: {string.Join(", ", fs.Elements)}");

            // 4) pick the first real, numeric root
            var firstReal = fs.Elements
                              .FirstOrDefault(r => r.EvaluableNumerical && r.IsFinite)
                              ?.EvalNumerical()
                          ?? throw new InvalidOperationException("No real roots found.");

            Console.Error.WriteLine($"[SolveFormulaForX] First real root: {firstReal}");
            return (double)firstReal;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SolveFormulaForX] ERROR: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    // --------------------------- solveLP ---------------------------
    [McpServerTool,
     Description("Solve a small LP each bound must be singularly given, e.g. \"Maximize 5A + 3B; A >= 8; A <= 20; B >= 3; B <= 30; A + B <= 40\"")]
    public static string SolveLP(
        [Description("Mini-LP string; e.g. 'Maximize 2y + x; x <= 10; y <= 8; x + y <= 12'")]
        string lpIn = "Maximize X; X in R; 0 <= X <= 23")
    {
        Console.Error.WriteLine($"[SolveLP] Invoked with: '{lpIn}'");
        try
        {
            // Split into clauses
            var parts = lpIn.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim()).ToArray();
            if (parts.Length < 2)
                throw new ArgumentException("LP text is incomplete.");

            // Objective parsing
            var objMatch = Regex.Match(parts[0], @"^(Maximize|Minimize)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!objMatch.Success)
                throw new ArgumentException("Objective must be 'Maximize ...' or 'Minimize ...'.");
            bool maximize = objMatch.Groups[1].Value.StartsWith("Max", StringComparison.OrdinalIgnoreCase);
            var objExpr = objMatch.Groups[2].Value;
            Console.Error.WriteLine($"[SolveLP] Objective: {objExpr}");
            // Parse objective terms
            var termPattern = new Regex(@"([+-]?\s*\d*\.?\d*)\s*([A-Za-z]\w*)");
            var coeffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in termPattern.Matches(objExpr))
            {
                var coefStr = m.Groups[1].Value.Replace(" ", "");
                var varName = m.Groups[2].Value;
                double coef = string.IsNullOrEmpty(coefStr) || coefStr == "+" ? 1
                              : coefStr == "-" ? -1
                              : double.Parse(coefStr);
                coeffs[varName] = coeffs.GetValueOrDefault(varName) + coef;
                Console.Error.WriteLine($"[SolveLP] Parsed objective term: {coef} * {varName}");
            }

            var solver = Solver.CreateSolver("GLOP");
            // Create variables
            var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);
            foreach (var varName in coeffs.Keys)
            {
                vars[varName] = solver.MakeNumVar(double.NegativeInfinity, double.PositiveInfinity, varName);
                Console.Error.WriteLine($"[SolveLP] Created variable: {varName}");
            }

            // Constraints parsing
            for (int i = 1; i < parts.Length; i++)
            {
                var c = parts[i]; // Do not replace < or >
                Console.Error.WriteLine($"[SolveLP] Parsing constraint: {c}");
                var cm = Regex.Match(c, "^(.+?)(<=|>=|<|>|=)(.+)$");
                if (!cm.Success)
                    throw new ArgumentException($"Invalid constraint: '{c}'");
                var left = cm.Groups[1].Value.Trim();
                var op = cm.Groups[2].Value;
                var right = double.Parse(cm.Groups[3].Value.Trim());
                Console.Error.WriteLine($"[SolveLP] Constraint left: '{left}', op: '{op}', right: {right}");
                // Parse left side terms
                var linCoeffs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in termPattern.Matches(left))
                {
                    var cs = m.Groups[1].Value.Replace(" ", "");
                    var vn = m.Groups[2].Value;
                    double co = string.IsNullOrEmpty(cs) || cs == "+" ? 1
                                : cs == "-" ? -1
                                : double.Parse(cs);
                    linCoeffs[vn] = linCoeffs.GetValueOrDefault(vn) + co;
                    if (!vars.ContainsKey(vn))
                    {
                        vars[vn] = solver.MakeNumVar(double.NegativeInfinity, double.PositiveInfinity, vn);
                        Console.Error.WriteLine($"[SolveLP] Created variable (from constraint): {vn}");
                    }
                    Console.Error.WriteLine($"[SolveLP] Parsed constraint term: {co} * {vn}");
                }
                var constraint = solver.MakeConstraint(double.NegativeInfinity, double.PositiveInfinity, "");
                foreach (var kv in linCoeffs)
                    constraint.SetCoefficient(vars[kv.Key], kv.Value);
                const double epsilon = 1e-8;
                switch (op)
                {
                    case "<": constraint.SetBounds(double.NegativeInfinity, right - epsilon); break;
                    case "<=": constraint.SetBounds(double.NegativeInfinity, right); break;
                    case ">": constraint.SetBounds(right + epsilon, double.PositiveInfinity); break;
                    case ">=": constraint.SetBounds(right, double.PositiveInfinity); break;
                    case "=":  constraint.SetBounds(right, right); break;
                }
                Console.Error.WriteLine($"[SolveLP] Added constraint: {left} {op} {right}");
            }

            // Build objective
            var objective = solver.Objective();
            foreach (var kv in coeffs)
            {
                objective.SetCoefficient(vars[kv.Key], maximize ? kv.Value : -kv.Value);
                Console.Error.WriteLine($"[SolveLP] Set objective coefficient: {kv.Key} = {(maximize ? kv.Value : -kv.Value)}");
            }
            objective.SetMaximization();

            var result = solver.Solve();
            Console.Error.WriteLine($"[SolveLP] Solver result status: {result}");
            if (result != Solver.ResultStatus.OPTIMAL)
                throw new InvalidOperationException("LP has no optimal solution.");

            var objVal = objective.Value();
            Console.Error.WriteLine($"[SolveLP] Optimal objective: {objVal}");
            // Collect variable values
            var varVals = vars.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.SolutionValue()}");
            var resultString = $"obj={objVal}; " + string.Join("; ", varVals);
            Console.Error.WriteLine($"[SolveLP] Solution: {resultString}");
            return resultString;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SolveLP] ERROR: {ex.GetType().Name}: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }
}
