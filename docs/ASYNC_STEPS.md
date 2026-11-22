# Asynchronous steps

Any step type (HTTP, WCF, GraphQL, SQL, File, Script, ActionManual, If, While, For, ForEach) can be marked **Run asynchronously**
(step settings form, or `async` = true in the config sheet; a missing column means false and is added when you tick the box).

* The runner **starts the step in the background and carries on** with the next step at once.
* The data row **ends only when all its asynchronous steps have ended**; their requests, assertions and timings are recorded as usual.
  Steps are listed in the order they started, so timings overlap.
* Later steps **do not wait** for it and there is no validation error. To use a value an asynchronous step extracts
  (`outputs`, `_Step.value_`, `<From_response_…>`), give the later step a **Wait (ms)** long enough: the values that finished
  steps extracted are handed over after the wait. Without it the later step fails with "Missing request variable".
* An asynchronous step that fails with **Stop on failure** ticked stops the steps that have not started yet (steps already running are not undone).
  The row is failed because the step's result is a failure.
* On an If / While / For / ForEach step the whole block with its inner steps runs in the background. An async step inside a block that
  already runs in the background is run normally (in sequence inside that block).
* The Stop button cancels running asynchronous steps too.
* Rows keep their own mode (sequential or parallel); async only concerns the steps of one row.
* In the tree an asynchronous step shows `⇉ async`.
