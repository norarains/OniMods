using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class QueryContractRegression
{
    private static int checks;
    private static readonly List<string> failures = new List<string>();

    internal static void Run()
    {
        checks = 0; failures.Clear();
        Case("identity and boolean predicates", TestPredicates);
        Case("native status IDs and lazy detail", TestNativeStatusMembership);
        Case("exact typed array membership", TestStringArrayMembership);
        Case("invalid grammar and typed fields", TestInvalidQueries);
        Case("aggregates retain full input", TestAggregates);
        Case("pagination and lazy evaluation", TestPaginationAndLaziness);
        Case("identifier normalization", TestIdentifierCase);
        Case("bounded evaluation", TestBudgets);
        Case("sorting time budget", TestSortBudget);
        Case("request selectors", TestArguments);
        Case("threshold units", TestThresholds);
        Case("configuration preview gate", TestMutationGate);
        if (failures.Count > 0)
            throw new InvalidOperationException("Query contract failures:\n" + string.Join("\n", failures));
        Console.WriteLine("Typed query and configuration contract: " + checks + " checks passed");
    }

    private static void TestArguments()
    {
        QueryArguments.Validate(JObject.Parse("{action:'schema',dataset:'buildings'}"));
        QueryArguments.Validate(JObject.Parse("{action:'select',query:'SELECT id FROM buildings'}"));
        string sql = QueryArguments.WarningSql(JObject.Parse("{id:11053,limit:1,offset:2}"), 1);
        Check(sql.Contains("worldId = 1 AND id = 11053") && sql.EndsWith("LIMIT 1 OFFSET 2"), "warning selectors and pagination survive forwarding");
        Check(!QueryArguments.WarningSql(JObject.Parse("{areaId:'other-world'}"), 1).Contains("worldId = 1"), "area determines world unless caller explicitly adds world filter");
        foreach (string invalid in new[] {
            "{action:'schema',query:'ignored'}", "{action:'select',query:'SELECT id FROM items',worldId:1}",
            "{action:'select',query:'SELECT id FROM items',dataset:'items'}", "{action:'select',query:5}"
        })
        {
            bool rejected = false;
            try { QueryArguments.Validate(JObject.Parse(invalid)); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "query rejects misleading arguments: " + invalid);
        }
        foreach (string invalid in new[] {
            "{query:'SELECT id FROM building_warnings',id:1}", "{query:'SELECT id FROM building_warnings',limit:1}",
            "{unknown:true}", "{id:'broken'}", "{limit:201}", "{offset:-1}", "{areaId:5}"
        })
        {
            bool rejected = false;
            try { QueryArguments.WarningSql(JObject.Parse(invalid), 1); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "warning view rejects ignored/invalid selector: " + invalid);
        }
    }

    private static FactDataset Dataset(IEnumerable<Dictionary<string, object>> rows,
        Action<string> reading = null)
    {
        return new FactDataset("items", new[] {
            new FactField("id", "number"), new FactField("elementId", "string"),
            new FactField("stored", "boolean"), new FactField("massKg", "number", "kg"),
            new FactField("worldId", "number"), new FactField("x", "number", "cell"),
            new FactField("y", "number", "cell"), new FactField("name", "string"),
            new FactField("tags", "string[]"), new FactField("skills", "string[]"), new FactField("capabilities", "string[]"),
            new FactField("heavy", "string", cost: "expensive")
        }.Concat(FactQueryStatusSnapshot.Fields), () => rows.Select(values => new FactRow(field => {
            reading?.Invoke(field);
            return values.TryGetValue(field, out object value) ? value : null;
        })));
    }

    private static Dictionary<string, object> Row(int id, string element = "Aluminum", double? mass = 10,
        bool stored = false, int world = 1)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal) {
            ["id"] = id, ["elementId"] = element, ["massKg"] = mass, ["stored"] = stored,
            ["worldId"] = world, ["x"] = id, ["y"] = 0, ["name"] = "Item " + id,
            ["heavy"] = "yes", ["statusIds"] = new[] { "Blocked" },
            ["statuses"] = new JArray(new JObject { ["id"] = "Blocked", ["text"] = "阻塞" })
        };
    }

    private static void TestNativeStatusMembership()
    {
        int nativeReads = 0, textReads = 0;
        var native = new[] {
            new { id = 1, world = 1, statuses = new[] { "Blocked" }, text = "Flooded in localized text only" },
            new { id = 2, world = 1, statuses = new[] { "Flooded" }, text = "建筑被淹没" },
            new { id = 3, world = 1, statuses = new[] { "FloodedCritical" }, text = "Flooded" },
            new { id = 4, world = 2, statuses = new[] { "Flooded" }, text = "Another world" },
            new { id = 5, world = 1, statuses = new[] { "Flooded", "Flooded" }, text = "两个原生状态条目" },
            new { id = 6, world = 1, statuses = new string[0], text = "Healthy" }
        };
        var fields = new[] {
            new FactField("id", "number"), new FactField("worldId", "number"), new FactField("prefabId", "string"),
            new FactField("position", "object")
        }.Concat(FactQueryStatusSnapshot.Fields);
        var data = new FactDataset("building_warnings", fields, () => native.Select(item => {
            var snapshot = new FactQueryStatusSnapshot(() => {
                nativeReads++;
                return item.statuses.Select(id => new FactQueryStatus(id, "Bad", () => { textReads++; return item.text; }));
            });
            return new FactRow(field => {
                switch (field)
                {
                    case "id": return item.id;
                    case "worldId": return item.world;
                    case "prefabId": return "Electrolyzer";
                    case "position": return new { x = item.id, y = 87 };
                    case "statusIds": return snapshot.Ids;
                    case "statuses": return snapshot.Details;
                    default: throw new ArgumentException(field);
                }
            });
        }));
        var buildings = new FactDataset("buildings", fields, data.Rows);
        buildings.IndexedRows = predicate => null;
        Check(Ids(Query("SELECT id FROM buildings LIMIT 2", buildings)).SequenceEqual(new[] { 1, 2 })
            && nativeReads == 0 && textReads == 0, "unrequested status fields do not enumerate or localize native data");
        data.Required = row => row.Get("worldId").Value<int>() == 1 && (row.Get("statusIds") as JArray).Count > 0;
        Reject(() => Query("SELECT id,prefabId,position,statuses FROM building_warnings WHERE statuses CONTAINS 'Flood' LIMIT 15", data),
            "known-bad object-array substring query");
        Check(nativeReads == 0 && textReads == 0, "wrong status predicate fails before touching native status entries");
        var result = Query("SELECT id,prefabId,position,statuses FROM building_warnings WHERE statusIds CONTAINS 'Flooded' AND worldId = 1 LIMIT 15", data);
        Check(Ids(result).SequenceEqual(new[] { 2, 5 }), "native canonical status membership ignores substring IDs, localized text and other worlds");
        Check(nativeReads == 5 && textReads == 3, "IDs enumerate once per scoped row and localized details resolve only for projected matches");
        Check((string)result["rows"][0][3][0]["id"] == "Flooded"
            && (string)result["rows"][0][3][0]["text"] == "建筑被淹没"
            && (string)result["rows"][0][3][0]["type"] == "Bad", "existing native status detail objects retain their shape");
        nativeReads = textReads = 0;
        var ids = Query("SELECT id,statusIds FROM building_warnings WHERE statusIds CONTAINS 'flooded'", data);
        Check(Ids(ids).SequenceEqual(new[] { 2, 5 }) && textReads == 0, "case-insensitive exact statusIds membership never resolves localized names");
        Check(ids["rows"][1][1].Count() == 1, "compact statusIds deduplicates canonical IDs without dropping detailed entries");
        nativeReads = textReads = 0;
        Check(Ids(Query("SELECT id FROM building_warnings WHERE statusIds CONTAINS 'Flood'", data)).Length == 0
            && textReads == 0, "partial native ID does not match an array member");
        nativeReads = textReads = 0;
        var legacy = FactQueryParser.Parse("SELECT id FROM building_warnings WHERE has_status('Flooded')");
        Check(Ids(FactQueryExecutor.Execute(legacy, data)).SequenceEqual(new[] { 2, 5 }) && textReads == 0,
            "existing has_status uses the same lazy canonical ID snapshot");
        Check(Ids(FactQueryExecutor.Execute(legacy, data)).SequenceEqual(new[] { 2, 5 }), "validated native status plans can be reused");
        Check(Ids(Query("SELECT id FROM building_warnings WHERE has_status('flooded')", data)).Length == 0,
            "legacy has_status retains its case-sensitive native ID comparison");
        nativeReads = textReads = 0;
        var limited = Query("SELECT id,statusIds FROM building_warnings WHERE statusIds CONTAINS 'Flooded' LIMIT 1", data);
        Check(Ids(limited).SequenceEqual(new[] { 2 }) && (bool)limited["truncated"] && (int)limited["nextOffset"] == 1
            && textReads == 0, "status membership preserves bounded pagination without full warning reports");
    }

    private static void TestStringArrayMembership()
    {
        var rows = new[] { Row(1), Row(2), Row(3), Row(4), Row(5), Row(6) };
        rows[0]["skills"] = new[] { "Hauling2", "Engineering1" };
        rows[1]["skills"] = new[] { "Engineering10" };
        rows[2]["skills"] = new string[0];
        rows[3]["skills"] = null;
        rows[4]["skills"] = new JObject { ["id"] = "Engineering1" };
        rows[5]["skills"] = new JArray(5, JValue.CreateNull(), new JObject { ["id"] = "Engineering1" });
        rows[0]["capabilities"] = new[] { "threshold" };
        rows[1]["capabilities"] = new[] { "threshold_extra" };
        var data = Dataset(rows);
        Check(Ids(Query("SELECT id FROM items WHERE skills CONTAINS 'engineering1'", data)).SequenceEqual(new[] { 1 }),
            "skill membership requires a complete string member, never object serialization or a numeric/null value");
        Check(Ids(Query("SELECT id FROM items WHERE capabilities CONTAINS 'THRESHOLD'", data)).SequenceEqual(new[] { 1 }),
            "capability membership uses the same exact typed array contract");
        Check(Ids(Query("SELECT id FROM items WHERE skills IS NULL", data)).SequenceEqual(new[] { 4 }),
            "null skill evidence stays unknown rather than an empty or text array");
        Check(Ids(Query("SELECT id FROM items WHERE skills CONTAINS 'Engineering'", data)).Length == 0,
            "string array substring is not a supported membership test");
        foreach (string invalid in new[] {
            "skills CONTAINS 1", "skills CONTAINS true", "skills = 'Engineering1'", "capabilities > 'threshold'",
            "statusIds CONTAINS 1", "statusIds = 'Blocked'"
        }) Reject(() => Query("SELECT id FROM items WHERE " + invalid, data), invalid);
        var legacy = new FactDataset("items", new[] { new FactField("id", "number"), new FactField("statuses", "array") },
            () => new[] { new FactRow(field => field == "id" ? (object)7 : new JArray(new JObject { ["id"] = "Flooded" })) });
        Check(Ids(Query("SELECT id FROM items WHERE has_status('Flooded')", legacy)).SequenceEqual(new[] { 7 }),
            "object-array-only datasets retain existing has_status compatibility");
    }

    private static JObject Query(string sql, FactDataset dataset, int milliseconds = 5000)
        => FactQueryExecutor.Execute(FactQueryParser.Parse(sql), dataset, milliseconds);

    private static int[] Ids(JObject result) => result["rows"].Select(row => row[0].Value<int>()).ToArray();

    private static void TestPredicates()
    {
        var source = new[] { Row(1), Row(2, "AluminumOre"), Row(3, stored: true), Row(4, world: 2) };
        var data = Dataset(source);
        Check(Ids(Query("SELECT id FROM items WHERE elementId = 'Aluminum' AND stored = false AND worldId = 1", data))
            .SequenceEqual(new[] { 1 }), "exact identity excludes ore, storage and other worlds");
        Check(Ids(Query("SELECT id FROM items WHERE elementId CONTAINS 'aluminum'", data)).Length == 4,
            "substring predicate is explicit and case insensitive");
        Check(Ids(Query("SELECT id FROM items WHERE id = 1 OR id = 2 AND stored = true", data)).SequenceEqual(new[] { 1 }),
            "AND binds more tightly than OR");
        Check(Ids(Query("SELECT id FROM items WHERE (id = 1 OR id = 2) AND stored = true", data)).Length == 0,
            "parentheses change boolean grouping");
        Check(Ids(Query("SELECT id FROM items WHERE id >= 2 AND id <> 3 AND id <= 4", data)).SequenceEqual(new[] { 2, 4 }),
            "ordered numeric and unequal predicates compose");
        source[0]["tags"] = new[] { "Metal" };
        Check(Ids(Query("SELECT id FROM items WHERE tags CONTAINS 'metal'", data)).SequenceEqual(new[] { 1 }), "string arrays have explicit membership predicates");
        source[0]["name"] = "D'Angelo 氧气";
        Check(Ids(Query("SELECT id FROM items WHERE name = 'D''Angelo 氧气'", data)).SequenceEqual(new[] { 1 }),
            "quoted literals preserve escaped apostrophes and Unicode");
        Check(Ids(Query("SELECT id FROM items WHERE near(0,0,2)", data)).SequenceEqual(new[] { 1, 2 }),
            "near uses geometric distance including the radius boundary");
        Check(Ids(Query("SELECT id FROM items WHERE has_status('Blocked')", data)).Length == 4,
            "native status predicate matches canonical status IDs");
        var plan = FactQueryParser.Parse("SELECT id FROM items WHERE in_area('module')", name =>
        {
            Check(name == "module", "area name is forwarded exactly");
            return new FactPredicate { Field = "id", Operator = "=", Value = new JValue(2) };
        });
        Check(Ids(FactQueryExecutor.Execute(plan, data)).SequenceEqual(new[] { 2 }), "named area predicate participates in normal filtering");
        data.Required = row => row.Get("worldId").Value<int>() == 1;
        Check(Ids(Query("SELECT id FROM items WHERE id = 4 OR stored = true", data)).SequenceEqual(new[] { 3 }),
            "query OR cannot bypass the dataset's mandatory scope filter");
    }

    private static void TestInvalidQueries()
    {
        string[] invalid = {
            "DELETE FROM items", "SELECT id FROM items; SELECT id FROM items", "SELECT id FROM items -- comment",
            "SELECT id FROM items WHERE id = 1; DROP TABLE items", "SELECT * FROM items", "SELECT AVG(massKg) FROM items",
            "SELECT SUM(*) FROM items", "SELECT id FROM items WHERE id = NaN", "SELECT id FROM items WHERE id = NULL",
            "SELECT id FROM items WHERE id IN (1,2)", "SELECT id FROM items WHERE near(1,2,-1)",
            "SELECT id FROM items WHERE in_area('missing')", "SELECT id FROM items LIMIT 0", "SELECT id FROM items LIMIT 201",
            "SELECT id FROM items LIMIT 1.5", "SELECT id FROM items OFFSET -1", "SELECT id FROM items OFFSET 50001",
            "SELECT id FROM items WHERE statuses CONTAINS 'Blocked'",
            "SELECT unknown FROM items", "SELECT id FROM items WHERE unknown = 1", "SELECT SUM(elementId) FROM items",
            "SELECT id FROM items WHERE id = '1'", "SELECT id FROM items WHERE stored = 1",
            "SELECT id FROM items WHERE stored > false", "SELECT id FROM items WHERE massKg CONTAINS '1'",
            "SELECT id FROM items ORDER BY missing", "SELECT id, id FROM items", "SELECT id AS x, massKg AS X FROM items",
            "SELECT id, SUM(massKg) FROM items", "SELECT id FROM items GROUP BY id",
            "SELECT COUNT(*) FROM items GROUP BY statuses", "SELECT COUNT(*) FROM items ORDER BY massKg"
        };
        int enumerated = 0;
        var data = new FactDataset("items", Dataset(new[] { Row(1) }).Fields.Values, () =>
        { enumerated++; return new[] { new FactRow(field => null) }; });
        foreach (string sql in invalid) Reject(() => Query(sql, data), sql);
        Check(enumerated == 0, "all invalid syntax/field/type queries fail before enumerating native data");
        Reject(() => FactQueryParser.Parse("SELECT id FROM items WHERE " + new string('(', 17) + "id=1" + new string(')', 17)),
            "nesting budget");
        Reject(() => FactQueryParser.Parse(new string(' ', 8193)), "input length budget");
    }

    private static void TestAggregates()
    {
        var data = Dataset(new[] { Row(1, mass: 100), Row(2, mass: 80), Row(3, mass: 20, stored: true),
            Row(4, "AluminumOre", 3000), Row(5, "AluminumOre", 500) });
        var total = Query("SELECT COUNT(*) AS stacks, SUM(massKg) AS kg FROM items WHERE elementId = 'Aluminum' LIMIT 1", data);
        Check((int)total["rows"][0][0] == 3 && (double)total["rows"][0][1] == 200 && (int)total["scanned"] == 5,
            "LIMIT bounds output, never aggregate input");
        var grouped = Query("SELECT elementId, COUNT(*) AS stacks, SUM(massKg) AS kg FROM items GROUP BY elementId ORDER BY kg DESC LIMIT 1", data);
        Check((string)grouped["rows"][0][0] == "AluminumOre" && (double)grouped["rows"][0][2] == 3500
            && (bool)grouped["truncated"] && (int)grouped["nextOffset"] == 1, "group totals include full dataset before sorted pagination");
        var page2 = Query("SELECT elementId, SUM(massKg) AS kg FROM items GROUP BY elementId ORDER BY kg DESC LIMIT 1 OFFSET 1", data);
        Check((string)page2["rows"][0][0] == "Aluminum" && (double)page2["rows"][0][1] == 200 && !(bool)page2["truncated"],
            "last aggregate page has no spurious continuation");
        var unknown = Query("SELECT COUNT(*) AS allRows, COUNT(massKg) AS known, SUM(massKg) AS total FROM items",
            Dataset(new[] { Row(1, mass: null), Row(2, mass: 5) }));
        Check((int)unknown["rows"][0][0] == 2 && (int)unknown["rows"][0][1] == 1 && unknown["rows"][0][2].Type == JTokenType.Null,
            "missing evidence cannot turn a partial SUM into a complete numeric total");
        Check((int)unknown["unknownInputs"]["total"] == 1 && (int)unknown["unknownInputs"]["known"] == 1,
            "unknown aggregate inputs are counted explicitly");
        var empty = Query("SELECT COUNT(*) AS n, SUM(massKg) AS mass FROM items", Dataset(new Dictionary<string, object>[0]));
        Check((int)empty["rows"][0][0] == 0 && empty["rows"][0][1].Type == JTokenType.Null, "empty COUNT is zero but empty SUM is null");
        var outsidePage = Query("SELECT elementId, SUM(massKg) AS mass FROM items GROUP BY elementId ORDER BY elementId LIMIT 1",
            Dataset(new[] { Row(1, "A", 10), Row(2, "Z", null) }));
        Check((int)outsidePage["unknownInputs"]["mass"] == 1, "unknown evidence survives even outside the returned group page");
        var nullable = new[] { Row(1, mass: null), Row(2, mass: 0), Row(3, mass: 1) };
        Check(Ids(Query("SELECT id FROM items WHERE massKg IS NULL", Dataset(nullable))).SequenceEqual(new[] { 1 }), "IS NULL selects unknown values");
        Check(Ids(Query("SELECT id FROM items WHERE massKg != 0", Dataset(nullable))).SequenceEqual(new[] { 3 }), "unknown is not treated as unequal-to-zero evidence");
    }

    private static void TestPaginationAndLaziness()
    {
        int expensive = 0, reads = 0;
        var data = Dataset(Enumerable.Range(1, 6).Select(i => Row(i)), field =>
        { reads++; if (field == "heavy") expensive++; });
        var page = Query("SELECT id FROM items LIMIT 2 OFFSET 2", data);
        Check(Ids(page).SequenceEqual(new[] { 3, 4 }) && (bool)page["truncated"] && (int)page["nextOffset"] == 4,
            "unsorted pagination returns the requested rows and next offset");
        Check(page["matched"].Type == JTokenType.Null && (int)page["scanned"] == 5, "early limit does not fabricate total matches");
        Check(expensive == 0 && reads == 2, "unprojected fields and off-page rows are not materialized");
        var last = Query("SELECT id FROM items LIMIT 2 OFFSET 4", data);
        Check(Ids(last).SequenceEqual(new[] { 5, 6 }) && !(bool)last["truncated"] && last["nextOffset"] == null,
            "exact last page has no continuation");
        var beyond = Query("SELECT id FROM items LIMIT 2 OFFSET 100", data);
        Check(!beyond["rows"].HasValues && !(bool)beyond["truncated"], "offset past dataset ends cleanly");
        var sorted = Query("SELECT id FROM items ORDER BY massKg DESC LIMIT 2", data);
        Check((int)sorted["scanned"] == 6 && expensive == 0, "sorting scans required fields but not unrelated expensive fields");
        expensive = 0;
        var filtered = Query("SELECT id, heavy FROM items WHERE heavy = 'yes' AND id = 2", data);
        Check(Ids(filtered).SequenceEqual(new[] { 2 }) && expensive == 1, "cheap AND predicate runs first and projected field reuses predicate cache");
        expensive = 0;
        data.Required = row => row.Get("heavy").Value<string>() == "yes";
        var scoped = Query("SELECT id FROM items WHERE id = 2", data);
        Check(Ids(scoped).SequenceEqual(new[] { 2 }) && expensive == 1,
            "cheap query scope precedes expensive mandatory view predicates such as native warning collection");
    }

    private static void TestIdentifierCase()
    {
        var result = Query("select ID, MASSKG from ITEMS where ELEMENTID = 'Aluminum' order by ID", Dataset(new[] { Row(7, mass: 11) }));
        Check(result["rows"].Count() == 1 && (int)result["rows"][0][0] == 7 && (double)result["rows"][0][1] == 11,
            "case-insensitive validated identifiers must use canonical native field names");
        var grouped = Query("SELECT ELEMENTID, SUM(MASSKG) AS TotalKg FROM ITEMS GROUP BY ELEMENTID ORDER BY totalkg DESC",
            Dataset(new[] { Row(1, "A", 5), Row(2, "A", 6), Row(3, "B", 2) }));
        Check((string)grouped["columns"][1] == "TotalKg" && (string)grouped["rows"][0][0] == "A"
            && (double)grouped["rows"][0][1] == 11, "canonical field lookup preserves explicit alias and grouped sort semantics");
    }

    private static void TestBudgets()
    {
        Reject(() => Query("SELECT COUNT(*) AS n FROM items", Dataset(Enumerable.Range(1, FactQueryExecutor.MaxScan + 1).Select(i => Row(i)))),
            "scan overflow must fail rather than return a partial aggregate");
        Reject(() => Query("SELECT elementId, COUNT(*) AS n FROM items GROUP BY elementId", Dataset(Enumerable.Range(1, 5001).Select(i => Row(i, "E" + i)))),
            "group cardinality is bounded");
        var huge = Row(1); huge["heavy"] = new string('x', FactQueryExecutor.MaxOutputChars + 1);
        Reject(() => Query("SELECT heavy FROM items", Dataset(new[] { huge })), "single-field output budget");
        Reject(() => Query("SELECT heavy FROM items", Dataset(new[] { Row(1) }, field => Thread.Sleep(20)), 1),
            "slow field read cannot return a successful over-budget result");
    }

    private static void TestSortBudget()
    {
        int expensiveReads = 0;
        var rows = Enumerable.Range(1, 100).Select(i => Row(i)).ToArray();
        foreach (var row in rows) row["heavy"] = "value " + row["id"];
        var data = Dataset(rows, field => {
            if (field == "heavy") { expensiveReads++; Thread.Sleep(5); }
        });
        Reject(() => Query("SELECT id FROM items ORDER BY heavy LIMIT 1", data, 10),
            "sort detects elapsed budget");
        Check(expensiveReads < 10,
            "sort must check the time budget during field evaluation, not read every expensive key before rejecting");
    }

    private static void TestThresholds()
    {
        Func<float, float> forbidden = value => { throw new InvalidOperationException("Unexpected display conversion"); };
        Check(Near(ThresholdValuePolicy.ToNative(280, null, true, 0, 1000, forbidden), 280), "default threshold is native, not displayed Celsius");
        Check(Near(ThresholdValuePolicy.ToNative(7, "C", true, 0, 1000, forbidden), 280.15f), "explicit Celsius converts to Kelvin");
        Check(Near(ThresholdValuePolicy.ToNative(32, "F", true, 0, 1000, forbidden), 273.15f), "explicit Fahrenheit converts to Kelvin");
        Check(Near(ThresholdValuePolicy.ToNative(280, "K", true, 0, 1000, forbidden), 280), "Kelvin is already native");
        Check(Near(ThresholdValuePolicy.ToNative(10, "display", true, 0, 1000, value => value + 273.15f), 283.15f),
            "display unit delegates to native display conversion");
        Check(Near(ThresholdValuePolicy.ToNative(-2, "native", false, 0, 100, forbidden), 0)
            && Near(ThresholdValuePolicy.ToNative(102, "native", false, 0, 100, forbidden), 100), "native bounds clamp intended thresholds");
        foreach (string unit in new[] { "K", "C", "F", "bogus" })
            Reject(() => ThresholdValuePolicy.ToNative(1, unit, false, 0, 100, forbidden), "unit incompatible with non-temperature threshold: " + unit);
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(() => ThresholdValuePolicy.ToNative(value, "native", true, 0, 1000, forbidden), "non-finite threshold");
        Reject(() => ThresholdValuePolicy.ToNative(1, "display", true, 0, 1000, value => float.NaN), "non-finite converted threshold");
        Check(ThresholdValuePolicy.NativeUnit("LogicTemperatureSensor") == "K"
            && ThresholdValuePolicy.NativeUnit("LogicPressureSensor") == "native", "read receipts identify native temperature units");
    }

    private static void TestMutationGate()
    {
        foreach (bool confirm in new[] { false, true })
        {
            var args = new JObject { ["dryRun"] = true, ["confirm"] = confirm };
            string original = args.ToString();
            var result = ConfigMutation.Preview(args, new { id = 5 }, new { threshold = 280.15, unit = "K" });
            var body = JObject.Parse(result.Content[0].Text);
            Check(!result.IsError && (bool)body["dryRun"] && !(bool)body["changed"]
                && (double)body["intended"]["threshold"] == 280.15, "dry-run is a terminal nonmutation receipt even with confirm=" + confirm);
            Check(args.ToString() == original, "preview does not rewrite caller arguments");
        }
        Check(ConfigMutation.Preview(new JObject(), new { id = 5 }, new { enabled = true }).IsError, "unconfirmed commit returns an error gate");
        Check(ConfigMutation.Preview(new JObject { ["confirm"] = true }, new { id = 5 }, new { enabled = true }) == null,
            "only confirmed commit passes the mutation gate");
    }

    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .001f;
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "reject " + message);
    }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Case(string name, Action action)
    { try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message); } }
}
