var config = Runtime.Get("config");
var name = Runtime.Get("name")?.ToString() ?? "unknown";
Runtime.Set("processed", "true");
Runtime.Set("processedName", name.ToUpperInvariant());
Runtime.Log($"Processed file configuration for {name}.");
new { processed = true, name = name.ToUpperInvariant(), source = "file" }
