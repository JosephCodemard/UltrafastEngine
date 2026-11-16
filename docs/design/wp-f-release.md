# WP-F: release pipeline

Session 7. Needs the owner for the tag and PyPI approval. **Ask the owner about PyPI before starting** (account name, whether `ultrafast` is free, 2FA / API-token handling for `twine`).

1. Name check on PyPI and TestPyPI; if `ultrafast` is taken use `ultrafast-engine` as the distribution name, import name stays `ultrafast`.
2. Version 1.0.0 everywhere (done in WP-B); pre-release tag `v1.0.0rc1` first.
3. Per-platform wheels (macOS arm64, Windows x64, Linux x64 and arm64) with the NativeAOT library inside; extras `[cuda]`, `[opencl]`, `[fitting]`, `[hitran]` follow backlog decision 3. Zemax never in a wheel; test that planting a `ZOSAPI*.dll` fails the packager.
4. Run `release.yml` for the first time on the pre-release tag (it has never run); fix the fallout.
5. Upload to TestPyPI with `twine`, install into a fresh venv on each OS, run quickstart + strict example + `ultrafast devices` + `ultrafast agent install` + `ultrafast mcp` handshake.
6. Real upload with `twine` **only on the owner's explicit go-ahead**; the token comes from the owner's environment and never enters the repository or logs (trusted publishing is an alternative to discuss).
7. GitHub release (tag created by the owner) with notes from the 1.0.0 changelog entry, including the list of unverified inputs and the Zemax / Metal / GPU status.
