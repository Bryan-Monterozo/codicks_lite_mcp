# Codicks Lite macOS release packaging

Codicks Lite releases are now built directly from the current workspace. The
installer no longer embeds or reconstructs the C# source tree.

## Build a release locally

From the repository root:

```bash
bash installers/build_codicks-lite_v1_1_1_release_macos --clean
```

The default build targets the current Mac architecture and performs:

```text
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r <current mac RID> --self-contained true
package
SHA-256
```

Build both supported macOS packages:

```bash
bash installers/build_codicks-lite_v1_1_1_release_macos --clean --all
```

Build a specific architecture:

```bash
bash installers/build_codicks-lite_v1_1_1_release_macos --rid osx-arm64
bash installers/build_codicks-lite_v1_1_1_release_macos --rid osx-x64
```

Compile/package is the default. To add the full test suite as a release gate:

```bash
bash installers/build_codicks-lite_v1_1_1_release_macos --with-tests
```

The current legacy stdio integration tests still include pre-v1.1 assumptions about
starting writable, so use `--with-tests` after those scenarios are migrated to
the local OTP/session-control workflow.

## Output

Packages are written to:

```text
installers/dist/
├── codicks-lite-1.1.0-macos-arm64.tar.gz
├── codicks-lite-1.1.0-macos-arm64.sha256
├── codicks-lite-1.1.0-macos-x64.tar.gz
└── codicks-lite-1.1.0-macos-x64.sha256
```

Each archive contains:

```text
codicks-lite-1.1.0-macos-<arch>/
├── install
├── app/                  # compiled self-contained publish output
├── tools/
│   ├── codicks-lite-control
│   └── setup_config.sh
├── config/
│   └── agent.default.json
├── docs/
└── manifest.txt
```

The recipient does not need the Codicks Lite source tree and does not need a .NET
SDK/runtime because the published app is self-contained.

## Recipient installation

```bash
tar -xzf codicks-lite-1.1.0-macos-arm64.tar.gz
cd codicks-lite-1.1.0-macos-arm64
./install
```

Optionally enter tunnel configuration immediately:

```bash
./install --configure
```

Installed releases remain versioned under:

```text
~/Library/Application Support/CodicksLiteMcp/releases/
```

`current` selects the active release and `previous` preserves the rollback
target.

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
```

The local build script is the release authority. Future Codicks Lite versions should
update the source normally, then update/copy the release builder for the new
version instead of embedding the source files into a giant installer script.
