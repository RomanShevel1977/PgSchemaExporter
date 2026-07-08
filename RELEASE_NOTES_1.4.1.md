# Release Notes v1.4.1

## Bug fixes and reliability improvements

- Fixed schema watcher to properly handle cancellation during debounce window
- Added error handling for temporary directory cleanup failures
- Fixed null reference exception when reading subscription publications
- Added schema name normalization (trim whitespace, remove duplicates)
- Fixed test assertion case sensitivity for object kind names
