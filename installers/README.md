# Codicks Lite 1.2.1 macOS release packaging

Codicks Lite releases are built directly from the current workspace. The installer packages compiled/published output and does not embed or reconstruct the C# source tree.

## Build a release locally

From the repository root:

```bash
bash installers/build_codicks-lite_v1_2_1_release_macos --clean
```

Compile/package is the default. Add the full regression suite as a release gate with:

```bash
bash installers/build_codicks-lite_v1_2_1_release_macos --clean --with-tests
```

Build both supported macOS RIDs:

```bash
bash installers/build_codicks-lite_v1_2_1_release_macos --clean --all
```

Build one RID:

```bash
bash installers/build_codicks-lite_v1_2_1_release_macos --rid osx-arm64
bash installers/build_codicks-lite_v1_2_1_release_macos --rid osx-x64
```

The local builder performs restore/build, optional tests, self-contained publish, packaging, manifest creation, and SHA-256 generation.

## Output

```text
installers/dist/
├── codicks-lite-1.2.1-macos-arm64.tar.gz
├── codicks-lite-1.2.1-macos-arm64.sha256
├── codicks-lite-1.2.1-macos-x64.tar.gz
└── codicks-lite-1.2.1-macos-x64.sha256
```

Each archive contains:

```text
codicks-lite-1.2.1-macos-<arch>/
├── install
├── app/                  # self-contained publish output
├── tools/
│   ├── codicks-lite-control
│   └── setup_config.sh
├── config/
│   └── agent.default.json
├── docs/
└── manifest.txt
```

The package includes the session, execution, security, review-workflow, review-hardening, and 1.2.1 release-acceptance documentation.

Recipients do not need the repository source tree or a .NET SDK/runtime.

## Recipient installation

```bash
tar -xzf codicks-lite-1.2.1-macos-arm64.tar.gz
cd codicks-lite-1.2.1-macos-arm64
./install
```

Optional tunnel configuration:

```bash
./install --configure
```

## Upgrade behavior

Installed releases live under:

```text
~/Library/Application Support/CodicksLiteMcp/releases/
```

`current` selects the active release and `previous` preserves the rollback target.

An existing:

```text
~/Library/Application Support/CodicksLiteMcp/config/agent.json
```

is preserved. The installer does not automatically grant `update` or `execute`, and it does not automatically enable Host/Sandbox process execution.

Restart `tunnel-client` after installation so the next stdio child uses the new active release.

## Installed command surface

```text
codicks-lite setup
codicks-lite config
codicks-lite config-show
codicks-lite config-edit
codicks-lite apply
codicks-lite run
codicks-lite doctor
codicks-lite doctor-local
codicks-lite agent-config

codicks-lite status
codicks-lite lock
codicks-lite read --otp <OTP> --for <minutes>
codicks-lite full --otp <OTP> --for <minutes>

codicks-lite version
codicks-lite releases
codicks-lite rollback
codicks-lite paths
codicks-lite host
```

`doctor-local` treats the optional Apple `container` runtime as informational; its absence does not make the base Codicks installation unhealthy.

## Release acceptance

See:

```text
docs/v1.2.1-release-acceptance.md
```

After installing, verify:

```bash
codicks-lite version
codicks-lite doctor-local
codicks-lite status
```

Expected installed version:

```text
Codicks Lite MCP 1.2.1
```
