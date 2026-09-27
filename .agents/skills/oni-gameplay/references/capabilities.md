# Capability discovery

Call `server_control domain=catalog action=manifest detail=brief` once per connection/restart and cache capabilities. Only publicTools are directly callable; supported batchOperations use `server_control domain=batch action=call_many calls=[{tool:name,args:{...}}]`. Rediscover after missing-tool/schema errors. Skip edit marks unless editMarks=true.

For colony facts use the query entry in the [control skill](../SKILL.md#read-facts); dataset schemas are on demand. Use catalog search only for unfamiliar operations. Specialized colony/dupe/world diagnostics and management remain internal batch operations.

Use full for narrow batch read details (`results[].result`); summary for writes. Outer dryRun checks routing only; child dryRun performs game validation. A healthy boundedContinue round needs no additional read. Ordinary reads omit tutorials; request includeHelp once when needed. Preserve explicit world/bounds for edits; viewport reads follow the camera.
