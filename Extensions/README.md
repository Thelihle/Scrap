# Extensions

This folder contains extension methods used throughout the application.

## Structure

- `MappingExtensions/`: Contains extensions for mapping between domain models and view models
  - `PendingApprovalMappingExtensions.cs`: Maps between PendingScrapEntry and PendingApprovalViewModel

## Usage

To use these extensions in your code:

```csharp
using SCP_Control.Extensions.MappingExtensions;

// Then you can use the extensions:
var viewModel = pendingScrapEntry.ToViewModel();
var viewModels = pendingScrapEntries.ToViewModels();
```

## Guidelines

When adding new extensions:
1. Place them in an appropriate subfolder based on their purpose
2. Use clear, descriptive names that indicate what they do
3. Add null checks and appropriate error handling
4. Document any non-obvious behavior