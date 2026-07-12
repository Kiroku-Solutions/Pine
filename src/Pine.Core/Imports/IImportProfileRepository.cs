using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Importing;

/// <summary>
/// Repository interface for managing import profiles.
/// </summary>
public interface IImportProfileRepository
{
    Task<ImportProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ImportProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
