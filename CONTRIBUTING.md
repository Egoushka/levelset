# Contributing

Levelset is source-available under FSL-1.1-ALv2 (see `LICENSE.md`); each release turns into Apache 2.0 two years
after it ships. The owner is the sole copyright holder.

- Sign off every commit (`git commit -s`): that is the Developer Certificate of Origin
  (https://developercertificate.org). There is no CLA.
- One task, one branch, one pull request. CI must be green: `dotnet build` and `dotnet test`.
- Every check in the catalog has an ID, a severity, a `spec` link and a fix hint, and a test with a good and a bad
  fixture. A check backed by a draft spec is never an error by default.
- The checker never executes a tool, submits a form or sends a payment. A change that does is rejected.
- `agent-checks` runs on commits (`lefthook install`) and on pull requests.
