# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [0.2.1] - 2026-04-03

### Added

- Added the unified `Flow Studio` onboarding/operations surface as the primary end-to-end UI flow.
- Added diagnostics-first navigation with direct entry points for OAuth/API troubleshooting, fallback resolution, importer issue handoff, and auto-layout diagnostics.

### Changed

- Updated menu taxonomy to center workflow around `Window/FigmaImporter/Help/Flow Studio`.
- Hardened sync-in automation defaults: auto dispatch now requires a shared secret, while source allowlist stays optional.

### Fixed

- Fixed `FigmaImporterHelpWindow` stage selector color assignment to compile correctly across Unity editor environments.
- Fixed event flow step-state tracking so duplicate-allowed steps still update chain state deterministically.

## [0.2.0] - 2026-03-28

### Added

- Added package-level open-source documentation (`README.md`, `Documentation~`).
- Added package license file (`LICENSE.md`).
- Added package changelog for release tracking.

### Changed

- Updated package metadata for public Git URL distribution.
- Added package compatibility/documentation/license URLs in `package.json`.
- Removed internal fingerprint field from `package.json`.

## [0.1.2] - 2026-03-23

### Added

- Initial importer package baseline in this repository.
