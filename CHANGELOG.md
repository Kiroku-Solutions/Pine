# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- **Envelope encryption for the vault key (TD-06).** The vault's database key is now a random
  256-bit data key stored only in encrypted form, wrapped with AES-256-GCM under an auth key that
  Argon2id derives from the master password. The wrap is bound to every other field of
  `pine.vault.json`, so a wrap cannot be moved between vaults and no header field can be edited
  without breaking it.
- **Change your master password**, from Settings → Master Password. The vault is not re-encrypted
  and does not close: only the key that protects the data key is replaced. Held to the same
  password-strength floor as vault creation.
- Initial project scaffold for Phase 0.

### Changed
- **BREAKING: vaults created by earlier builds can no longer be opened.** The vault header is now
  version 2, and version-1 headers (and the older `pine.salt`-only layout) are refused with a
  message saying so rather than being upgraded in place. This was taken deliberately while no vault
  exists outside a developer machine; it removes the un-enveloped key path entirely instead of
  carrying it alongside the new one. Create a new vault and re-enter your data.
- An incorrect master password is now detected by the key envelope's authentication tag, before any
  database file is opened, instead of surfacing as a SQLCipher error.

### Known limitations
- Changing your password rotates the key that protects your data key, **not** the data key itself.
  Someone who had already extracted the data key would still hold a working key. Rotating it
  requires re-encrypting the whole vault and is not yet implemented (TD-86).
- Biometric and platform-keychain unlock remain unavailable. The new envelope makes them possible;
  nothing implements them yet (TD-87).
- The experimental browser build cannot open vaults at all: its runtime does not provide AES-GCM
  (TD-88).
